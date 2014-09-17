import getStatusDebugChangesCommand = require("commands/getStatusDebugChangesCommand");
import appUrl = require("common/appUrl");
import database = require("models/database");
import viewModelBase = require("viewmodels/viewModelBase");


class statusDebugChanges extends viewModelBase {
    data = ko.observable<Array<statusDebugChangesDto>>();

    activate(args) {
        super.activate(args);

        this.activeDatabase.subscribe(() => this.fetchStatusDebugChanges());
        return this.fetchStatusDebugChanges();
    }

    fetchStatusDebugChanges(): JQueryPromise<Array<statusDebugChangesDto>> {
        var db = this.activeDatabase();
        if (db) {
            return new getStatusDebugChangesCommand(db)
                .execute()
                .done((results: Array<statusDebugChangesDto>) => this.data(results));
        }

        return null;
    }
}

export = statusDebugChanges;