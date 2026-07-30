using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text;

namespace SQLAZOR.Portal.Components.Layout
{
    public partial class CuPopover : ComponentBase, IAsyncDisposable
    {
        // ========== PARAMETERS ==========
        [Parameter] public RenderFragment? ChildContent { get; set; }

        // Content
        [Parameter] public string? Title { get; set; }
        [Parameter] public RenderFragment? TitleContent { get; set; }
        [Parameter] public string? Content { get; set; }
        [Parameter] public RenderFragment? BodyContent { get; set; }

        // Behavior
        [Parameter] public string Placement { get; set; } = "top"; // top, bottom, left, right
        [Parameter] public string Trigger { get; set; } = "hover";  // hover, click, focus, manual
        [Parameter] public bool Disabled { get; set; } = false;

        // Timing
        [Parameter] public int ShowDelay { get; set; } = 0;
        [Parameter] public int HideDelay { get; set; } = 100;
        [Parameter] public int? AutoHide { get; set; } = null;
        [Parameter] public bool Animated { get; set; } = true;

        // Content rendering
        [Parameter] public bool Html { get; set; } = false;
        [Parameter] public bool Sanitize { get; set; } = true;

        // Positioning
        [Parameter] public int Offset { get; set; } = 0;
        [Parameter] public int MaxWidth { get; set; } = 276;
        [Parameter] public int? MaxHeight { get; set; } = null;

        // Styling
        [Parameter] public string? CssClass { get; set; }

        // Interaction
        [Parameter] public bool CloseOnEscape { get; set; } = true;
        [Parameter] public bool CloseOnOutsideClick { get; set; } = true;
        [Parameter] public bool HideOnScroll { get; set; } = true;

        // Events
        [Parameter] public EventCallback OnShow { get; set; }
        [Parameter] public EventCallback OnShown { get; set; }
        [Parameter] public EventCallback OnHide { get; set; }
        [Parameter] public EventCallback OnHidden { get; set; }

        // ========== PRIVATE STATE ==========
        private ElementReference triggerElement;
        private ElementReference popoverElement;
        private IJSObjectReference? module;
        private DotNetObjectReference<CuPopover>? dotNetRef;

        private string popoverId = $"popover-{Guid.NewGuid():N}";
        private bool IsVisible = false;
        private bool IsPositioned = false;
        private bool pendingPosition = false;
        private string ActualPlacement = "top";
        private Timer? autoHideTimer;

        // ========== LIFECYCLE ==========
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                module = await JS.InvokeAsync<IJSObjectReference>(
                    "import", "./Components/Layout/CuPopover.razor.js");
                dotNetRef = DotNetObjectReference.Create(this);

                await module.InvokeVoidAsync("initialize", triggerElement, dotNetRef, new
                {
                    trigger = Trigger,
                    showDelay = ShowDelay,
                    hideDelay = HideDelay
                });
            }

            if (pendingPosition && module != null)
            {
                pendingPosition = false;

                // Position the popover
                ActualPlacement = await module.InvokeAsync<string>(
                    "positionPopover",
                    triggerElement,
                    popoverElement,
                    Placement,
                    Offset);

                // Setup global event handlers
                await module.InvokeVoidAsync(
                    "setupEventHandlers",
                    triggerElement,
                    popoverElement,
                    dotNetRef,
                    new
                    {
                        trigger = Trigger,
                        closeOnEscape = CloseOnEscape,
                        closeOnOutsideClick = CloseOnOutsideClick,
                        hideOnScroll = HideOnScroll
                    });

                IsPositioned = true;
                StateHasChanged();
                await OnShown.InvokeAsync();

                // Setup auto-hide
                if (AutoHide.HasValue)
                {
                    autoHideTimer = new Timer(async _ =>
                    {
                        await InvokeAsync(Hide);
                    }, null, AutoHide.Value, Timeout.Infinite);
                }
            }
        }

        // ========== PUBLIC API ==========
        public async Task Show()
        {
            if (Disabled || IsVisible) return;

            await OnShow.InvokeAsync();
            IsVisible = true;
            IsPositioned = false;
            pendingPosition = true;
            StateHasChanged();
        }

        public async Task Hide()
        {
            if (!IsVisible) return;

            await OnHide.InvokeAsync();

            autoHideTimer?.Dispose();
            autoHideTimer = null;

            if (module != null)
            {
                try
                {
                    await module.InvokeVoidAsync("cleanupEventHandlers", popoverElement);
                }
                catch (JSDisconnectedException) { }
            }

            IsPositioned = false;
            StateHasChanged();

            if (Animated)
            {
                await Task.Delay(150); // Wait for fade-out
            }

            IsVisible = false;
            StateHasChanged();
            await OnHidden.InvokeAsync();
        }

        public async Task Toggle()
        {
            if (IsVisible)
                await Hide();
            else
                await Show();
        }

        // ========== JS CALLABLE METHODS ==========
        [JSInvokable]
        public async Task JsShow() => await Show();

        [JSInvokable]
        public async Task JsHide() => await Hide();

        [JSInvokable]
        public async Task JsToggle() => await Toggle();

        [JSInvokable]
        public async Task JsReposition()
        {
            if (IsVisible && IsPositioned && module != null)
            {
                ActualPlacement = await module.InvokeAsync<string>(
                    "positionPopover",
                    triggerElement,
                    popoverElement,
                    Placement,
                    Offset);
                StateHasChanged();
            }
        }

        // ========== HELPERS ==========
        private string GetPopoverStyle()
        {
            var sb = new StringBuilder();
            sb.Append($"max-width: {MaxWidth}px;");

            if (MaxHeight.HasValue)
            {
                sb.Append($"max-height: {MaxHeight.Value}px;overflow-y: auto;");
            }

            return sb.ToString();
        }

        // ========== DISPOSAL ==========
        public async ValueTask DisposeAsync()
        {
            autoHideTimer?.Dispose();

            if (module != null)
            {
                try
                {
                    await module.InvokeVoidAsync("dispose", triggerElement);
                    await module.InvokeVoidAsync("cleanupEventHandlers", popoverElement);
                    await module.DisposeAsync();
                }
                catch (JSDisconnectedException) { }
            }

            dotNetRef?.Dispose();
        }
    }
}