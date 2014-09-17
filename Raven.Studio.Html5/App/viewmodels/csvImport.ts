﻿import viewmodelBase = require("viewmodels/viewModelBase");
import saveCsvFileCommand = require("commands/saveCsvFileCommand");

class csvImport extends viewmodelBase {
    
    hasFileSelected = ko.observable(false);
    isImporting = ko.observable(false);

    fileSelected(args: any) {
        this.hasFileSelected(true);
    }

    importCsv() {
        if (!this.isImporting()) {
            this.isImporting(true);

            var formData = new FormData();
            var fileInput = <HTMLInputElement>document.querySelector("#csvFilePicker");
            formData.append("file", fileInput.files[0]);

            new saveCsvFileCommand(formData, fileInput.files[0].name, this.activeDatabase())
                .execute()
                .always(() => this.isImporting(false));
        }
    }

}

export = csvImport; 