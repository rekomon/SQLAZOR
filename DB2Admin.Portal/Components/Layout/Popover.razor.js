let popoverInstances = new WeakMap();

export function initializePopover(element, dotNetRef, trigger) {
    const events = {
        hover: ['mouseenter', 'mouseleave'],
        click: ['click'],
        focus: ['focus', 'blur']
    };

    const [showEvent, hideEvent] = events[trigger] || events.hover;

    const handlers = {
        show: () => dotNetRef.invokeMethodAsync('ShowPopover'),
        hide: () => dotNetRef.invokeMethodAsync('HidePopover')
    };

    element.addEventListener(showEvent, handlers.show);
    if (hideEvent) {
        element.addEventListener(hideEvent, handlers.hide);
    } else {
        // For click trigger, hide on document click
        setTimeout(() => {
            document.addEventListener('click', (e) => {
                if (!element.contains(e.target)) {
                    handlers.hide();
                }
            });
        }, 0);
    }

    popoverInstances.set(element, { showEvent, hideEvent, handlers });
}

export function disposePopover(element) {
    const instance = popoverInstances.get(element);
    if (instance) {
        element.removeEventListener(instance.showEvent, instance.handlers.show);
        if (instance.hideEvent) {
            element.removeEventListener(instance.hideEvent, instance.handlers.hide);
        }
        popoverInstances.delete(element);
    }
}