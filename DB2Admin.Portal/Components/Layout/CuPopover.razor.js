const instances = new WeakMap();

export function initialize(element, dotNetRef, options) {
    const instance = {
        dotNetRef,
        options,
        showTimeout: null,
        hideTimeout: null,
        isShown: false
    };

    instances.set(element, instance);

    if (options.trigger === 'manual') return;

    const triggers = getTriggerEvents(options.trigger);

    triggers.forEach(event => {
        element.addEventListener(event, (e) => handleTrigger(event, instance, e));
    });
}

function getTriggerEvents(trigger) {
    const map = {
        'hover': ['mouseenter', 'mouseleave'],
        'click': ['click'],
        'focus': ['focus', 'blur'],
        'hover focus': ['mouseenter', 'mouseleave', 'focus', 'blur']
    };
    return map[trigger] || map['hover'];
}

function handleTrigger(event, instance, e) {
    clearTimeouts(instance);

    switch (event) {
        case 'mouseenter':
        case 'focus':
            instance.showTimeout = setTimeout(
                () => instance.dotNetRef.invokeMethodAsync('JsShow'),
                instance.options.showDelay || 0
            );
            break;

        case 'mouseleave':
        case 'blur':
            instance.hideTimeout = setTimeout(
                () => instance.dotNetRef.invokeMethodAsync('JsHide'),
                instance.options.hideDelay || 0
            );
            break;

        case 'click':
            e.stopPropagation();
            instance.dotNetRef.invokeMethodAsync('JsToggle');
            break;
    }
}

function clearTimeouts(instance) {
    if (instance.showTimeout) {
        clearTimeout(instance.showTimeout);
        instance.showTimeout = null;
    }
    if (instance.hideTimeout) {
        clearTimeout(instance.hideTimeout);
        instance.hideTimeout = null;
    }
}

// ========== POSITIONING ==========
export function positionPopover(trigger, popover, preferredPlacement, offset) {
    const triggerRect = trigger.getBoundingClientRect();
    const popoverRect = popover.getBoundingClientRect();
    const vw = window.innerWidth;
    const vh = window.innerHeight;
    const margin = 5;
    const arrowSize = 8;

    const calculate = (p) => ({
        top: p === 'top' ? triggerRect.top - popoverRect.height - offset :
            p === 'bottom' ? triggerRect.bottom + offset :
                triggerRect.top + (triggerRect.height - popoverRect.height) / 2,
        left: p === 'left' ? triggerRect.left - popoverRect.width - offset :
            p === 'right' ? triggerRect.right + offset :
                triggerRect.left + (triggerRect.width - popoverRect.width) / 2
    });

    let placement = preferredPlacement;
    let pos = calculate(placement);
    let top = pos.top;
    let left = pos.left;

    const overflows = (t, l) =>
        t < margin ||
        t + popoverRect.height > vh - margin ||
        l < margin ||
        l + popoverRect.width > vw - margin;

    // Try flipping
    if (overflows(top, left)) {
        const flips = { top: 'bottom', bottom: 'top', left: 'right', right: 'left' };
        const flipped = flips[placement];
        const flippedPos = calculate(flipped);

        if (!overflows(flippedPos.top, flippedPos.left)) {
            placement = flipped;
            top = flippedPos.top;
            left = flippedPos.left;
        } else {
            // Constrain to viewport
            top = Math.max(margin, Math.min(top, vh - popoverRect.height - margin));
            left = Math.max(margin, Math.min(left, vw - popoverRect.width - margin));
        }
    }

    // Apply position
    popover.style.position = 'fixed';
    popover.style.top = `${top}px`;
    popover.style.left = `${left}px`;

    // Position arrow
    const arrow = popover.querySelector('.popover-arrow');
    if (arrow) {
        ['top', 'left', 'right', 'bottom'].forEach(p => arrow.style[p] = '');

        const triggerCenterX = triggerRect.left + triggerRect.width / 2;
        const triggerCenterY = triggerRect.top + triggerRect.height / 2;

        switch (placement) {
            case 'top':
                arrow.style.bottom = `-${arrowSize / 2}px`;
                arrow.style.left = `${Math.max(arrowSize, Math.min(popoverRect.width - arrowSize * 2, triggerCenterX - left - arrowSize / 2))}px`;
                break;
            case 'bottom':
                arrow.style.top = `-${arrowSize / 2}px`;
                arrow.style.left = `${Math.max(arrowSize, Math.min(popoverRect.width - arrowSize * 2, triggerCenterX - left - arrowSize / 2))}px`;
                break;
            case 'left':
                arrow.style.right = `-${arrowSize / 2}px`;
                arrow.style.top = `${Math.max(arrowSize, Math.min(popoverRect.height - arrowSize * 2, triggerCenterY - top - arrowSize / 2))}px`;
                break;
            case 'right':
                arrow.style.left = `-${arrowSize / 2}px`;
                arrow.style.top = `${Math.max(arrowSize, Math.min(popoverRect.height - arrowSize * 2, triggerCenterY - top - arrowSize / 2))}px`;
                break;
        }
    }

    return placement;
}

// ========== EVENT HANDLERS ==========
export function setupEventHandlers(trigger, popover, dotNetRef, options) {
    // Escape key
    let keyHandler = null;
    if (options.closeOnEscape) {
        keyHandler = (e) => {
            if (e.key === 'Escape') {
                dotNetRef.invokeMethodAsync('JsHide');
            }
        };
        document.addEventListener('keydown', keyHandler);
    }

    // Outside click
    let outsideClickHandler = null;
    if (options.closeOnOutsideClick && options.trigger === 'click') {
        outsideClickHandler = (e) => {
            if (!trigger.contains(e.target) && !popover.contains(e.target)) {
                dotNetRef.invokeMethodAsync('JsHide');
            }
        };
        setTimeout(() => document.addEventListener('click', outsideClickHandler), 0);
    }

    // Scroll
    let scrollHandler = null;
    if (options.hideOnScroll) {
        scrollHandler = () => {
            dotNetRef.invokeMethodAsync('JsHide');
        };
        window.addEventListener('scroll', scrollHandler, true);
    } else {
        scrollHandler = () => {
            dotNetRef.invokeMethodAsync('JsReposition');
        };
        window.addEventListener('scroll', scrollHandler, true);
    }

    // Resize
    const resizeHandler = () => {
        dotNetRef.invokeMethodAsync('JsReposition');
    };
    window.addEventListener('resize', resizeHandler);

    // Store for cleanup
    popover._cleanup = () => {
        if (keyHandler) document.removeEventListener('keydown', keyHandler);
        if (outsideClickHandler) document.removeEventListener('click', outsideClickHandler);
        if (scrollHandler) window.removeEventListener('scroll', scrollHandler, true);
        window.removeEventListener('resize', resizeHandler);
    };
}

export function cleanupEventHandlers(popover) {
    if (popover && popover._cleanup) {
        popover._cleanup();
        delete popover._cleanup;
    }
}

export function dispose(element) {
    const instance = instances.get(element);
    if (instance) {
        clearTimeouts(instance);
        instances.delete(element);
    }
}