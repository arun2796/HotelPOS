// Print bridge (Phase 22 P6): opens a PDF held in memory in a new tab. The app fetches the PDF with its bearer token,
// so there is no URL a plain window.open could use. Returns false when the browser blocked the window.
window.hotelposPrint = {
    openPdf: function (bytes, fileName) {
        var blob = new Blob([bytes], { type: "application/pdf" });
        var url = URL.createObjectURL(blob);
        var win = window.open(url, "_blank");
        if (!win) {
            URL.revokeObjectURL(url);
            return false;
        }
        setTimeout(function () { URL.revokeObjectURL(url); }, 120000);
        return true;
    }
};
