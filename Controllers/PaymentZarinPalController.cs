using System.Collections.Generic;
using System.Web.Mvc;
using Nop.Core;
using Nop.Plugin.Payments.ZarinPal.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Payments;
using Nop.Services.Stores;
using Nop.Web.Framework.Controllers;
using Nop.Core.Domain.Payments;
using Nop.Services.Orders;
using Nop.Core.Data;
using Nop.Core.Domain.Orders;
using System.Linq;
using System.Text;
using System;
using Nop.Core.Domain.Logging;
using Nop.Services.Logging;

namespace Nop.Plugin.Payments.ZarinPal.Controllers
{
    public class PaymentZarinPalController : BasePaymentController
    {
        private readonly IWorkContext _workContext;
        private readonly IStoreService _storeService;
        private readonly ISettingService _settingService;
        private readonly ILocalizationService _localizationService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IPaymentService _paymentService;
        private readonly PaymentSettings _paymentSettings;
        private readonly IOrderService _orderService;
        private readonly IRepository<Order> _orderRepository;
        private readonly ILogger _logger;

        public PaymentZarinPalController(IWorkContext workContext,
            IStoreService storeService,
            ISettingService settingService,
            ILocalizationService localizationService,
            IOrderProcessingService orderProcessingService,
            IRepository<Order> orderRepository,
            PaymentSettings paymentSettings,
            IOrderService orderService,
            ILogger logger,
            IPaymentService paymentService)
        {
            this._workContext = workContext;
            this._storeService = storeService;
            this._settingService = settingService;
            this._localizationService = localizationService;
            this._orderProcessingService = orderProcessingService;
            this._paymentService = paymentService;
            this._paymentSettings = paymentSettings;
            this._orderService = orderService;
            this._logger = logger;
            this._orderRepository = orderRepository;
        }

        [AdminAuthorize]
        [ChildActionOnly]
        public ActionResult Configure()
        {
            //load settings for a chosen store scope
            var storeScope = this.GetActiveStoreScopeConfiguration(_storeService, _workContext);
            var zarinPalPaymentSettings = _settingService.LoadSetting<ZarinPalPaymentSettings>(storeScope);

            var model = new ConfigurationModel();
            model.MerchantCode = zarinPalPaymentSettings.MerchantCode;
            model.CallbackUrl = zarinPalPaymentSettings.CallbackUrl;

            model.ActiveStoreScopeConfiguration = storeScope;
            if (storeScope > 0)
            {
                model.MerchantCode_OverrideForStore = _settingService.SettingExists(zarinPalPaymentSettings, x => x.MerchantCode, storeScope);
                model.CallbackUrl_OverrideForStore = _settingService.SettingExists(zarinPalPaymentSettings, x => x.CallbackUrl, storeScope);
            }

            return View("~/Plugins/Payments.ZarinPal/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AdminAuthorize]
        [ChildActionOnly]
        public ActionResult Configure(ConfigurationModel model)
        {
            if (!ModelState.IsValid)
                return Configure();

            //load settings for a chosen store scope
            var storeScope = this.GetActiveStoreScopeConfiguration(_storeService, _workContext);
            var zarinPalPaymentSettings = _settingService.LoadSetting<ZarinPalPaymentSettings>(storeScope);

            //save settings
            zarinPalPaymentSettings.MerchantCode = model.MerchantCode;
            zarinPalPaymentSettings.CallbackUrl = model.CallbackUrl;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            _settingService.SaveSettingOverridablePerStore(zarinPalPaymentSettings, x => x.MerchantCode, model.MerchantCode_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(zarinPalPaymentSettings, x => x.CallbackUrl, model.CallbackUrl_OverrideForStore, storeScope, false);

            //now clear settings cache
            _settingService.ClearCache();

            SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }

        [ChildActionOnly]
        public ActionResult PaymentInfo()
        {
            var model = new PaymentInfoModel();

            //set postback values
            var form = this.Request.Form;
            //model.PurchaseOrderNumber = form["PurchaseOrderNumber"];

            return View("~/Plugins/Payments.ZarinPal/Views/PaymentInfo.cshtml", model);
        }

        [NonAction]
        public override IList<string> ValidatePaymentForm(FormCollection form)
        {
            var warnings = new List<string>();
            return warnings;
        }

        [NonAction]
        public override ProcessPaymentRequest GetPaymentInfo(FormCollection form)
        {
            var paymentInfo = new ProcessPaymentRequest();
            return paymentInfo;
        }

        [ValidateInput(false)]
        public ActionResult Return(FormCollection form)
        {
            try
            {
                string strAuthority = Request["Authority"];
                string strStatus = Request["Status"];

                if (string.IsNullOrEmpty(strAuthority) || string.IsNullOrEmpty(strStatus))
                {
                    // to do
                    // payment cancelled
                    return RedirectToRoute("Plugin.Payments.ZarinPal.PaymentCancelled", new { Msg = "خطا در داده‌های شبکه" });
                }

                if (strStatus != "OK")
                {
                    return RedirectToRoute("Plugin.Payments.ZarinPal.PaymentCancelled", new { Msg = "عملیات پرداخت موفقیت آمیز نبود" });
                }


                var processor = _paymentService.LoadPaymentMethodBySystemName("Payments.ZarinPal") as ZarinPalPaymentProcessor;
                if (processor == null || !processor.IsPaymentMethodActive(_paymentSettings) || !processor.PluginDescriptor.Installed)
                {
                    throw new NopException("ZarinPal module cannot be loaded");
                }


                if (processor.VerifyPayment(strAuthority, strStatus))
                {
                    // payment has been verified
                    var query = from or in _orderRepository.Table
                                where or.AuthorizationTransactionCode == strAuthority
                                select or;
                    Order order = query.FirstOrDefault();
                    var sb = new StringBuilder();
                    sb.AppendLine("ZarinPal Transaction Summary:");
                    sb.AppendLine("Reference Code: " + order.AuthorizationTransactionResult);

                    //order note
                    order.OrderNotes.Add(new OrderNote()
                    {
                        Note = sb.ToString(),
                        DisplayToCustomer = false,
                        CreatedOnUtc = DateTime.UtcNow
                    });
                    _orderService.UpdateOrder(order);

                    //mark order as paid
                    if (_orderProcessingService.CanMarkOrderAsPaid(order))
                    {
                        _orderProcessingService.MarkOrderAsPaid(order);
                    }
                    return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
                }
                else
                {
                    var query = from or in _orderRepository.Table
                                where or.AuthorizationTransactionCode == strAuthority
                                select or;
                    Order order = query.FirstOrDefault();
                    if (order != null)
                    {
                        //order note
                        order.OrderNotes.Add(new OrderNote()
                        {
                            Note = "اشکالی در عملیات پرداخت پیدا شد. عملیات وریفای پرداخت موفق نبود. ",// + processor.GetErrorDescription(Convert.ToInt32(strTransactionStatus)),
                            DisplayToCustomer = true,
                            CreatedOnUtc = DateTime.UtcNow
                        });
                        _orderService.UpdateOrder(order);
                    }
                    return RedirectToRoute("Plugin.Payments.ZarinPal.PaymentCancelled", new { Msg = "تایید پرداخت ناموفق" });
                }
            }
            catch (Exception ex)
            {
                _logger.InsertLog(LogLevel.Debug, "in ex:" + ex.Message);
                return RedirectToRoute("Plugin.Payments.ZarinPal.PaymentCancelled", new { Msg = "خطا در عملیات پرداخت" });
            }

        }


        [ValidateInput(false)]
        public ActionResult PaymentCancelled(string Msg)
        {
            PaymentCancelledModel model = new PaymentCancelledModel();
            model.Description = Msg;
            //return View("Nop.Plugin.Payments.ZarinPal.Views.PaymentZarinPal.PaymentCancelled"); // payment canceled.
            return View("~/Plugins/Payments.ZarinPal/Views/PaymentCancelled.cshtml", model);
        }

    }
}
