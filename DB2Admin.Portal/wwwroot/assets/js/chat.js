window.SQLAZORChat = {
    copyText: async function (text) {
        try {
            if (navigator.clipboard && window.isSecureContext) {
                await navigator.clipboard.writeText(text);
                return true;
            }
        } catch (e) {
            // fall through to the legacy path below
        }

        try {
            const area = document.createElement('textarea');
            area.value = text;
            area.setAttribute('readonly', '');
            area.style.position = 'fixed';
            area.style.top = '-1000px';
            document.body.appendChild(area);
            area.select();
            const copied = document.execCommand('copy');
            area.remove();
            return copied;
        } catch (e) {
            return false;
        }
    },

    downloadText: function (fileName, text, mime) {
        window.SQLAZORChat._download(new Blob([text], { type: mime || 'text/plain;charset=utf-8' }), fileName);
    },

    downloadBase64: function (fileName, base64, mime) {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        window.SQLAZORChat._download(new Blob([bytes], { type: mime || 'application/octet-stream' }), fileName);
    },

    _download: function (blob, fileName) {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 1500);
    }
};
