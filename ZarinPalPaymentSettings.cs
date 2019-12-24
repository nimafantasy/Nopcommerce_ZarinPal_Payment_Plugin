using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.ZarinPal
{
    public class ZarinPalPaymentSettings : ISettings
    {
        /// <summary>
        /// Gets or sets a value indicating 36 char-long merchant code provided by ZarinPal
        /// </summary>
        public string MerchantCode { get; set; }

        /// <summary>
        /// Gets or sets a value indicating the url to which user is transfered back from the payment page.
        /// </summary>
        public string CallbackUrl { get; set; }
    }
}
