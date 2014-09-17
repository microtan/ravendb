﻿import getStatusDebugSqlReplicationCommand = require("commands/getStatusDebugSqlReplicationCommand");
import appUrl = require("common/appUrl");
import database = require("models/database");
import dialogViewModelBase = require("viewmodels/dialogViewModelBase");
import dialog = require("plugins/dialog");
import getSqlReplicationStatsCommand = require("commands/getSqlReplicationStatsCommand");

class sqlReplicationStatsDialog extends dialogViewModelBase {
    repData = ko.observable<sqlReplicationStatisticsDto>();

    constructor(private activeDatabase: database, private sqlReplicationName:string) {
        super();
    }

    activate(args) {
        this.fetchSqlReplicationStats();
    }

    cancel() {
        dialog.close(this);
    }

    fetchSqlReplicationStats(): JQueryPromise<sqlReplicationStatisticsDto> {
        
        return new getSqlReplicationStatsCommand(this.activeDatabase, this.sqlReplicationName)
            .execute()
            .done((result: sqlReplicationStatisticsDto) => {
                this.repData(result);
            }).fail(() => {
            this.repData(null);
        });
        
        
    }
}

export = sqlReplicationStatsDialog;