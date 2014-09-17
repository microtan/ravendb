﻿import filesystem = require("models/filesystem/filesystem");
import datePickerBindingHandler = require("common/datePickerBindingHandler");
import searchDialogViewModel = require("viewmodels/filesystem/searchDialogViewModel");
import dialog = require("plugins/dialog");
import moment = require("moment");

class searchLastModifiedBetweenClause extends searchDialogViewModel {

    public applyFilterTask = $.Deferred();
    fromDate = ko.observable<Moment>();
    toDate = ko.observable<Moment>();
    fromDateText: KnockoutComputed<string>;
    toDateText: KnockoutComputed<string>;

    constructor() {
        super([]);

        this.inputs.push(<KnockoutComputed<string>> ko.computed(function () {
            $("#fromDate").focus();
            return this.fromDate() != null ? this.fromDate().format("YYYY/MM/DD H:mm:ss") : "";
        }, this));

        this.inputs.push(<KnockoutComputed<string>> ko.computed(function () {
            $("#toDate").focus();
            return this.toDate() != null ? this.toDate().format("YYYY/MM/DD H:mm:ss") : "";
        }, this));

        datePickerBindingHandler.install();
    }

    applyFilter() {
        if (this.fromDate() == null || this.toDate() == null)
            return false;
        var dates = "__modified:[" + this.fromDate().format("YYYY/MM/DD_H:mm:ss").replaceAll("/", "-").replaceAll(":", "-")
            + " TO " + this.toDate().format("YYYY/MM/DD_H:mm:ss").replaceAll("/", "-").replaceAll(":", "-") + "]";
        this.applyFilterTask.resolve(dates);
        
        this.close()
    }
}

export = searchLastModifiedBetweenClause;