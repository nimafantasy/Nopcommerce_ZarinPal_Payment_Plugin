using System.Web.Mvc;
using System.Web.Routing;
using Nop.Web.Framework.Mvc.Routes;

namespace Nop.Plugin.Payments.ZarinPal
{
    public partial class RouteProvider : IRouteProvider
    {
        public void RegisterRoutes(RouteCollection routes)
        {
            //Return
            routes.MapRoute("Plugin.Payments.ZarinPal.Return",
                 "Plugins/PaymentZarinPal/Return",
                 new { controller = "PaymentZarinPal", action = "Return" },
                 new[] { "Nop.Plugin.Payments.ZarinPal.Controllers" }
            );

            routes.MapRoute("Plugin.Payments.ZarinPal.PaymentCancelled",
                 "Plugins/PaymentZarinPal/PaymentCancelled",
                 new { controller = "PaymentZarinPal", action = "PaymentCancelled" },
                 new[] { "Nop.Plugin.Payments.ZarinPal.Controllers" }
            );
        }
        public int Priority
        {
            get
            {
                return 0;
            }
        }
    }
}
