window.deviceTransfer = {
    printPage() {
        window.print();
    },

    downloadJson(fileName, content) {
        const blob = new Blob([content], { type: "application/json" });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");

        link.href = url;
        link.download = fileName;
        link.click();
        URL.revokeObjectURL(url);
    },

    openFilePicker(elementId) {
        const input = document.getElementById(elementId);
        if (input) {
            input.value = "";
            input.click();
        }
    }
};
