﻿import app = require("durandal/app");
import dialog = require("plugins/dialog");
import viewModelBase = require("viewmodels/viewModelBase");
import dialogViewModelBase = require("viewmodels/dialogViewModelBase");
import serverBuildReminder = require("common/serverBuildReminder");

class latestBuildReminder extends dialogViewModelBase {

    public dialogTask = $.Deferred();
    mute = ko.observable<boolean>(false);

    constructor(private latestServerBuildResult: latestServerBuildVersionDto, elementToFocusOnDismissal?: string) {
        super(elementToFocusOnDismissal);

        this.mute.subscribe(() => {
            serverBuildReminder.mute(this.mute());
        });
    }

    detached() {
        super.detached();
        this.dialogTask.resolve();
    }

    close() {
        dialog.close(this);
    }
}

export = latestBuildReminder;