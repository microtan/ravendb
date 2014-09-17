﻿import app = require("durandal/app");
import viewModelBase = require("viewmodels/viewModelBase");
import adminLogsConfigureCommand = require("commands/adminLogsConfigureCommand");
import adminLogsClient = require("common/adminLogsClient");
import fileDownloader = require("common/fileDownloader");
import adminLogsConfigureDialog = require("viewmodels/adminLogsConfigureDialog");
import adminLogsConfig = require("models/adminLogsConfig");
import getSingleAuthTokenCommand = require("commands/getSingleAuthTokenCommand");
import adminLogsConfigEntry = require("models/adminLogsConfigEntry");
import appUrl = require('common/appUrl');

class adminLogs extends viewModelBase {
   
    adminLogsClient = ko.observable<adminLogsClient>(null);
    pendingLogs: logDto[] = [];
    keepDown = ko.observable(false);
    rawLogs = ko.observable<logDto[]>([]);
    intervalId: number;
    connected = ko.observable(false);
    logsContainer: Element;
    entriesCount = ko.computed(() => this.rawLogs().length);
    adminLogsConfig = ko.observable<adminLogsConfig>();


    canActivate(args): any {
        return true;
    }

    redraw() {
        if (this.pendingLogs.length > 0) {
            var pendingCopy = this.pendingLogs;
            this.pendingLogs = [];
            var logsAsText = "";
            pendingCopy.forEach(log => {
                var line = log.TimeStamp + ";" + log.Level.toUpperCase() + ";" +  log.Database + ";" + log.LoggerName + ";" + log.Message + (log.Exception || "") + "\n";
                logsAsText += line;
            });
            // text: allows us to escape values
            $("<div/>").text(logsAsText).appendTo("#rawLogsContainer pre");
            this.rawLogs().pushAll(pendingCopy);
            this.rawLogs.valueHasMutated();

            if (this.keepDown()) {
                var logsPre = document.getElementById('adminLogsPre');
                logsPre.scrollTop = logsPre.scrollHeight;
            }
        }
    }

    clearLogs() {
        this.pendingLogs = [];
        this.rawLogs([]);
        $("#rawLogsContainer pre").empty();
    }

    defaultLogsConfig() {
        var logConfig = new adminLogsConfig();
        logConfig.maxEntries(10000);
        logConfig.entries.push(new adminLogsConfigEntry("Raven.", "Info"));
        return logConfig;
    }

    configureConnection() {
        this.intervalId = setInterval(function () { this.redraw(); }.bind(this), 1000);

        var currentConfig = this.adminLogsConfig() ? this.adminLogsConfig().clone() : this.defaultLogsConfig();
        var adminLogsConfigViewModel = new adminLogsConfigureDialog(currentConfig);
        app.showDialog(adminLogsConfigViewModel);
        adminLogsConfigViewModel.configurationTask.done((x: any) => {
            this.adminLogsConfig(x);
            this.reconnect();
        });
    }

    connect() {
        if (!!this.adminLogsClient()) {
            this.reconnect();
            return;
        }
        if (!this.adminLogsConfig()) {
            this.configureConnection();
            return;
        }

        var tokenDeferred = $.Deferred();

        if (!this.adminLogsConfig().singleAuthToken()) {
            new getSingleAuthTokenCommand(appUrl.getSystemDatabase(), true)
                .execute()
                .done((tokenObject: singleAuthToken) => {
                    this.adminLogsConfig().singleAuthToken(tokenObject);
                    tokenDeferred.resolve();
                })
                .fail((e) => {
                    app.showMessage("You are not authorized to trace this resource", "Authorization error");
                });
        } else {
            tokenDeferred.resolve();
        }

        tokenDeferred.done(() => {
            this.adminLogsClient(new adminLogsClient(this.adminLogsConfig().singleAuthToken().Token));
            this.adminLogsClient().connect();
            var categoriesConfig = this.adminLogsConfig().entries().map(e => e.toDto());
            this.adminLogsClient().configureCategories(categoriesConfig);
            this.adminLogsClient().connectionOpeningTask.done(() => {
                this.connected(true);
                this.adminLogsClient().watchAdminLogs((event: logDto) => {
                    this.onLogMessage(event);
                });
            });
            this.adminLogsConfig().singleAuthToken(null);
        });
    }

    disconnect(): JQueryPromise<any> {
        if (!!this.adminLogsClient()) {
            this.adminLogsClient().dispose();
            return this.adminLogsClient().connectionClosingTask.then(() => {
                this.adminLogsClient(null);
                this.connected(false);
            });
        } else {
            app.showMessage("Cannot disconnect, connection does not exist", "Disconnect");
            return $.Deferred().reject();
        }
    }

    reconnect() {
        if (!this.adminLogsClient()) {
            this.connect();
        } else {
            this.disconnect().done(() => {
                this.connect();
            });
        }
    }

    attached() {
        this.logsContainer = document.getElementById("rawLogsContainer");
    }

    deactivate() {
        clearInterval(this.intervalId);
        if (this.adminLogsClient()) {
            this.adminLogsClient().dispose();
        }
    }

    detached() {
        super.detached();
        this.disposeAdminLogsClient();
    }

    disposeAdminLogsClient() {
        var client = this.adminLogsClient();
        if (client) {
            client.dispose();
        }
    }

    onLogMessage(entry: logDto) {
        if (this.entriesCount() + this.pendingLogs.length < this.adminLogsConfig().maxEntries()) {
            this.pendingLogs.push(entry);
        } else {
            // stop logging
            var client = this.adminLogsClient();
            this.connected(false);
            client.dispose();
        }
    }

    exportLogs() {
        fileDownloader.downloadAsJson(this.rawLogs(), "logs.json");
    }

    toggleKeepDown() {
        this.keepDown.toggle();
        if (this.keepDown() == true) {
            var logsPre = document.getElementById('adminLogsPre');
            logsPre.scrollTop = logsPre.scrollHeight;
        }
    }
}

export = adminLogs;