﻿import commandBase = require("commands/commandBase");
import database = require("models/database");
import sqlReplication = require("models/sqlReplication");


class simulateSqlReplicationCommand extends  commandBase{
    
    constructor(private db: database, private simulatedSqlReplication: sqlReplication, private documentId: string, private performRolledbackTransaction) {
        super();
    }

    execute(): JQueryPromise<sqlReplicationSimulationResultDto> {
        var args = {
            documentId: this.documentId,
            performRolledBackTransaction: this.performRolledbackTransaction,
            sqlReplication: JSON.stringify(this.simulatedSqlReplication.toDto())
        };

        return this.query<sqlReplicationSimulationResultDto>("/studio-tasks/simulate-sql-replication", args, this.db, null, 60000);
    }
}

export = simulateSqlReplicationCommand;