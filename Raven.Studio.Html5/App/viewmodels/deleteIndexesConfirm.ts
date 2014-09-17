import index = require("models/index");
import deleteIndexCommand = require("commands/deleteIndexCommand");
import dialog = require("plugins/dialog");
import database = require("models/database");
import dialogViewModelBase = require("viewmodels/dialogViewModelBase");
import messagePublisher = require("common/messagePublisher");

class deleteIndexesConfirm extends dialogViewModelBase {

    deleteTask = $.Deferred();
    title: string;

    constructor(private indexNames: string[], private db: database, title?) {
        super();

        if (!indexNames || indexNames.length === 0) {
            throw new Error("Indexes must not be null or empty.");
        }

        this.title = !!title ? title : indexNames.length == 1 ? 'Delete index?' : 'Delete indexes?';
    }

    deleteIndexes() {
        var deleteTasks = this.indexNames.map(name => new deleteIndexCommand(name, this.db).execute());
        var myDeleteTask = this.deleteTask;

        $.when.apply($, deleteTasks)
            .done(() => {
                if (this.indexNames.length > 1) {
                    messagePublisher.reportSuccess("Successfully deleted " + this.indexNames.length + " indexes!");
                }
                myDeleteTask.resolve(false);
            })
            .fail(()=> {
                myDeleteTask.reject();
            });
        dialog.close(this);
    }

    cancel() {
        this.deleteTask.resolve(true);
        dialog.close(this);
    }
}

export = deleteIndexesConfirm;