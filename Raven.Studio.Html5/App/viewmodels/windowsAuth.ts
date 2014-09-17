﻿import windowsAuthSetup = require("models/windowsAuthSetup");
import windowsAuthData = require("models/windowsAuthData");
import viewModelBase = require("viewmodels/viewModelBase");
import getWindowsAuthCommand = require("commands/getWindowsAuthCommand");
import shell = require("viewmodels/shell");
import database = require("models/database");

class windowsAuth extends viewModelBase {

    setup = ko.observable<windowsAuthSetup>().extend({ required: true });
    isSaveEnabled: KnockoutComputed<boolean>;
    isUsersSectionActive = ko.observable<boolean>(true);

    canActivate(args) {
        var deffered = $.Deferred();
        this.setup(new windowsAuthSetup({ RequiredUsers: [], RequiredGroups: [] }));
        this.fetchWindowsAuth().always(() => deffered.resolve({ can: true }));

        return deffered;
    }

    activate(args) {
        super.activate(args);

        this.dirtyFlag = new ko.DirtyFlag([this.setup]);
        this.isSaveEnabled = ko.computed(() => this.dirtyFlag().isDirty());
    }

    compositionComplete() {
        super.compositionComplete();
        $("form").on("keypress", 'input[name="databaseName"]', (e) => e.which != 13);
    }

    private fetchWindowsAuth(): JQueryPromise<any> {
        return new getWindowsAuthCommand()
            .execute()
            .done((result: windowsAuthSetup) => this.setup(result));
    }

    saveChanges() {
        require(["commands/saveWindowsAuthCommand"], saveWindowsAuthCommand => {
            new saveWindowsAuthCommand(this.setup().toDto())
                .execute()
                .done(() => this.dirtyFlag().reset());
        });
    }

    addUserSettings() {
        var newAuthData = windowsAuthData.empty();
        windowsAuthSetup.subscribeToObservableName(newAuthData, this.setup().requiredUsers);
        this.setup().requiredUsers.push(newAuthData);
    }

    removeUserSettings(data: windowsAuthData) {
        this.setup().requiredUsers.remove(data);
    }

    addGroupSettings() {
        var newAuthData = windowsAuthData.empty();
        windowsAuthSetup.subscribeToObservableName(newAuthData, this.setup().requiredGroups);
        this.setup().requiredGroups.push(newAuthData);
    }

    removeGroupSettings(data: windowsAuthData) {
        this.setup().requiredGroups.remove(data);
    }
}

export = windowsAuth;