using Microsoft.JSInterop;

namespace SQLAZOR.Portal.Helper
{
    public static class IJSRuntimeExtension
    {


        public static async Task ToggleTheme(this IJSRuntime jsRuntime)
        {
            await jsRuntime.InvokeVoidAsync("toggleTheme");
        }


        public static async Task InitializeTheme(this IJSRuntime jsRuntime)
        {
             await jsRuntime.InvokeVoidAsync("initialize");
        }

        public static async ValueTask<bool> IsDarkMode(this IJSRuntime jsRuntime)
        {
            return await jsRuntime.InvokeAsync<bool>("CheckThemeMode");
        }

        public static async Task ShowGenerateResultTab(this IJSRuntime jsRuntime)
        {
            await jsRuntime.InvokeVoidAsync("ShowGenerateResultTab");
        }
    }
}
