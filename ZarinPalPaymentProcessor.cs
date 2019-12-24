using System;
using System.Collections.Generic;
using System.Web.Routing;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Plugins;
using Nop.Plugin.Payments.ZarinPal.Controllers;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Logging;
using Nop.Core.Data;
using Nop.Core.Infrastructure;
using System.Linq;
using Nop.Web.Framework;
using Nop.Core.Domain.Logging;
using System.Web;
using System.Web.Mvc;

namespace Nop.Plugin.Payments.ZarinPal
{
    public class ZarinPalPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields
        private readonly ILocalizationService _localizationService;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly ISettingService _settingService;
        private readonly ZarinPalPaymentSettings _zarinPalPaymentSettings;
        private readonly ILogger _logger;
        private readonly IRepository<Order> _orderRepository;
        #endregion

        #region Ctor
        public ZarinPalPaymentProcessor(ILocalizationService localizationService,
            IOrderTotalCalculationService orderTotalCalculationService,
            ISettingService settingService,
            ZarinPalPaymentSettings zarinPalPaymentSettings,
            ILogger logger,
            IOrderService orderService)
        {
            this._localizationService = localizationService;
            this._orderTotalCalculationService = orderTotalCalculationService;
            this._settingService = settingService;
            this._zarinPalPaymentSettings = zarinPalPaymentSettings;
            this._logger = logger;
            _orderRepository = EngineContext.Current.Resolve<IRepository<Order>>();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.NewPaymentStatus = PaymentStatus.Pending;
            return result;
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            try
            {

                ZarinPalService.PaymentGatewayImplementationService zps = new ZarinPalService.PaymentGatewayImplementationService();

                string ItemsDescription = "";
                foreach (OrderItem item in postProcessPaymentRequest.Order.OrderItems)
                {
                    ItemsDescription += item.Product.ShortDescription + "; ";

                }
                int result = zps.PaymentRequest(_zarinPalPaymentSettings.MerchantCode, Convert.ToInt32(postProcessPaymentRequest.Order.OrderTotal / 10), ItemsDescription, "", "", _zarinPalPaymentSettings.CallbackUrl, out string Authority);


                if (result == 100) // sussessful
                {
                    if (Authority.Length.Equals(36))
                    {
                        // ok to proceed
                        // after getting the number check for duplicate in db in case of fraud

                        var query = from or in _orderRepository.Table
                                    where or.AuthorizationTransactionCode == Authority
                                    select or;

                        if (query.Count() > 0)
                        {
                            // THIS CODE EXISTS,   H A L T   O P E R A T I O N
                            postProcessPaymentRequest.Order.PaymentStatus = PaymentStatus.Pending;
                            return;
                        }
                        else
                        {
                            // NO PREVIOUS RECORD OF REFNUM, CLEAR TO PROCEED
                            postProcessPaymentRequest.Order.AuthorizationTransactionCode = Authority;
                            _orderRepository.Update(postProcessPaymentRequest.Order);

                            var remotePostHelper = new RemotePost();
                            remotePostHelper.FormName = "form1";
                            remotePostHelper.Url = "https://www.zarinpal.com/pg/StartPay/" + Authority;
                            //remotePostHelper.Add("RefId", strRefNum);
                            remotePostHelper.Post();


                        }
                    }

                }
                else
                {
                    _logger.Error("int returned from initial request is: " + result.ToString());
                    postProcessPaymentRequest.Order.PaymentStatus = PaymentStatus.Pending;
                    return;
                }
                //nothing
            }
            catch (Exception ex)
            {
                return;
            }
        }


        /// <summary>
        /// Returns boolean indicating whether payment has been accepted by zarinpal
        /// </summary>
        /// <param name="authority">36digit long code</param>
        /// <param name="status">status code</param>
        /// <returns>true - accepted; false - not accepted.</returns>
        public bool VerifyPayment(string authority, string status)
        {
            try
            {

                var query = from or in _orderRepository.Table
                            where or.AuthorizationTransactionCode == authority
                            select or;
                _logger.InsertLog(LogLevel.Debug, "in verify1 :"+ query.Count());
                ZarinPalService.PaymentGatewayImplementationService zps = new ZarinPalService.PaymentGatewayImplementationService();
                _logger.InsertLog(LogLevel.Debug, "in verify2 : merch code: "+ _zarinPalPaymentSettings.MerchantCode + " %% authority code: "+ authority + " %% order total: " + query.FirstOrDefault().OrderTotal);
                if (zps.PaymentVerification(_zarinPalPaymentSettings.MerchantCode, authority, Convert.ToInt32(query.FirstOrDefault().OrderTotal / 10), out long RefID).Equals(100))
                {
                    _logger.InsertLog(LogLevel.Debug, "in verify3");
                    query.FirstOrDefault().AuthorizationTransactionResult = RefID.ToString();
                    _logger.InsertLog(LogLevel.Debug, "in verify4");
                    _orderRepository.Update(query.FirstOrDefault());
                    _logger.InsertLog(LogLevel.Debug, "in verify5");
                    return true;
                }
                else
                {
                    _logger.InsertLog(LogLevel.Debug, "in verify: verification failed");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.InsertLog(LogLevel.Debug, "in verify: " + ex.Message);
                return false;
            }
            
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public bool HidePaymentMethod(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country

            //if (_zarinPalPaymentSettings.ShippableProductRequired && !cart.RequiresShipping())
            //    return true;

            return false;
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>Additional handling fee</returns>
        public decimal GetAdditionalHandlingFee(IList<ShoppingCartItem> cart)
        {
            //var result = this.CalculateAdditionalFee(_orderTotalCalculationService, cart,
            //    _zarinPalPaymentSettings.AdditionalFee, _zarinPalPaymentSettings.AdditionalFeePercentage);
            return 0;
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public CapturePaymentResult Capture(CapturePaymentRequest capturePaymentRequest)
        {
            var result = new CapturePaymentResult();
            result.AddError("Capture method not supported");
            return result;
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public RefundPaymentResult Refund(RefundPaymentRequest refundPaymentRequest)
        {
            var result = new RefundPaymentResult();
            result.AddError("Refund method not supported");
            return result;
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public VoidPaymentResult Void(VoidPaymentRequest voidPaymentRequest)
        {
            var result = new VoidPaymentResult();
            result.AddError("Void method not supported");
            return result;
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessRecurringPayment(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.AddError("Recurring payment not supported");
            return result;
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public CancelRecurringPaymentResult CancelRecurringPayment(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            var result = new CancelRecurringPaymentResult();
            result.AddError("Recurring payment not supported");
            return result;
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public bool CanRePostProcessPayment(Order order)
        {
            if (order == null)
                throw new ArgumentNullException("order");

            //it's not a redirection payment method. So we always return false
            return false;
        }

        /// <summary>
        /// Gets a route for provider configuration
        /// </summary>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetConfigurationRoute(out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "Configure";
            controllerName = "PaymentZarinPal";
            routeValues = new RouteValueDictionary { { "Namespaces", "Nop.Plugin.Payments.ZarinPal.Controllers" }, { "area", null } };
        }

        /// <summary>
        /// Gets a route for payment info
        /// </summary>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetPaymentInfoRoute(out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "PaymentInfo";
            controllerName = "PaymentZarinPal";
            routeValues = new RouteValueDictionary { { "Namespaces", "Nop.Plugin.Payments.ZarinPal.Controllers" }, { "area", null } };
        }

        /// <summary>
        /// Get type of controller
        /// </summary>
        /// <returns>Type</returns>
        public Type GetControllerType()
        {
            return typeof(PaymentZarinPalController);
        }

        /// <summary>
        /// Install the plugin
        /// </summary>
        public override void Install()
        {
            //settings
            _settingService.SaveSetting(new ZarinPalPaymentSettings());

            //locales
            this.AddOrUpdatePluginLocaleResource("Plugins.Payment.ZarinPal.MerchantCode", "Merchant Code");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payment.ZarinPal.MerchantCode.Hint", "36 character long code provided by ZarinPal");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payment.ZarinPal.CallbackUrl", "Callback Url");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payment.ZarinPal.CallbackUrl.Hint", "Url to which user is brought back after payment page work is done.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payment.ZarinPal.PaymentMethodDescription", "Payment via Zarinpal.ir a well-known payment gateway.");

            base.Install();
        }

        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<ZarinPalPaymentSettings>();

            //locales
            this.DeletePluginLocaleResource("Plugins.Payment.ZarinPal.MerchantCode");
            this.DeletePluginLocaleResource("Plugins.Payment.ZarinPal.MerchantCode.Hint");
            this.DeletePluginLocaleResource("Plugins.Payment.ZarinPal.CallbackUrl");
            this.DeletePluginLocaleResource("Plugins.Payment.ZarinPal.CallbackUrl.Hint");
            this.DeletePluginLocaleResource("Plugins.Payment.ZarinPal.PaymentMethodDescription");

            base.Uninstall();
        }


        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType
        {
            get { return RecurringPaymentType.NotSupported; }
        }

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType
        {
            get { return PaymentMethodType.Redirection; }
        }

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        public string PaymentMethodDescription
        {
            //return description of this payment method to be display on "payment method" checkout step. good practice is to make it localizable
            //for example, for a redirection payment method, description may be like this: "You will be redirected to PayPal site to complete the payment"
            get { return _localizationService.GetResource("Plugins.Payment.ZarinPal.PaymentMethodDescription"); }
        }

        #endregion
    }
}
