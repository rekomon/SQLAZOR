using System.Text.RegularExpressions;

namespace SQLAZOR.Services
{
    public class HtmlSanitizerService
    {
        private static readonly Regex ScriptTagRegex = new(
      @"<script\b[^<]*(?:(?!</script>)<[^<]*)*</script>",
      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EventHandlerRegex = new(
            @"\s*on\w+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex JavaScriptProtocolRegex = new(
            @"javascript\s*:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex IframeRegex = new(
            @"<iframe[^>]*>.*?</iframe>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        public string Sanitize(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;

            html = ScriptTagRegex.Replace(html, string.Empty);
            html = IframeRegex.Replace(html, string.Empty);
            html = EventHandlerRegex.Replace(html, string.Empty);
            html = JavaScriptProtocolRegex.Replace(html, string.Empty);

            return html;
        }
    }
}
