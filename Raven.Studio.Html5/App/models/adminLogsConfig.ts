import adminLogsConfigEntry = require("models/adminLogsConfigEntry");

class adminLogsConfig {
    entries = ko.observableArray<adminLogsConfigEntry>();
    maxEntries = ko.observable<number>();
    singleAuthToken = ko.observable<singleAuthToken>();

    clone(): adminLogsConfig {
        var newConfig = new adminLogsConfig();
        newConfig.maxEntries(this.maxEntries());
        newConfig.entries($.map(this.entries() || [], (e, idx) => e.clone()));
        return newConfig;
    }
}

export = adminLogsConfig;