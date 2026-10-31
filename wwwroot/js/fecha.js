// FechaInput: abre el calendario nativo del <input type="date"> oculto.
window.cfdiFecha = {
    abrir: function (elemento) {
        if (elemento && typeof elemento.showPicker === "function") {
            elemento.showPicker();
        } else if (elemento) {
            elemento.focus();
            elemento.click();
        }
    }
};
