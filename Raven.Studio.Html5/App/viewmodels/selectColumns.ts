import app = require("durandal/app");
import document = require("models/document");
import dialog = require("plugins/dialog");
import database = require("models/database");
import dialogViewModelBase = require("viewmodels/dialogViewModelBase");
import customColumns = require('models/customColumns');
import customColumnParams = require('models/customColumnParams');
import saveDocumentCommand = require('commands/saveDocumentCommand');
import deleteDocumentCommand = require('commands/deleteDocumentCommand');
import inputCursor = require('common/inputCursor');
import customFunctions = require('models/customFunctions');
import autoCompleterSupport = require('common/autoCompleterSupport');
import messagePublisher = require("common/messagePublisher");

class selectColumns extends dialogViewModelBase {

    private nextTask = $.Deferred<customColumns>();
    nextTaskStarted = false;
    private form: JQuery;

    private activeInput: JQuery;
    private autoCompleteBase = ko.observableArray<KnockoutObservable<string>>([]);
    private autoCompleteResults = ko.observableArray<KnockoutObservable<string>>([]);
    private completionSearchSubscriptions: Array<KnockoutSubscription> = [];
    private autoCompleterSupport: autoCompleterSupport;

    maxTableHeight = ko.observable<number>();
    lineHeight: number = 51;
    isScrollNeeded: KnockoutComputed<boolean>;

    constructor(private customColumns: customColumns, private customFunctions: customFunctions, private context, private database: database) {
        super();
        this.generateCompletionBase();
        this.regenerateBindingSubscriptions();
        this.monitorForNewRows();
        this.autoCompleterSupport = new autoCompleterSupport(this.autoCompleteBase, this.autoCompleteResults);

        this.maxTableHeight(Math.floor($(window).height() * 0.43));
        
        $(window).resize(() => {
            this.maxTableHeight(Math.floor($(window).height() * 0.43));
            this.alignBoxVertically();
        });

        this.isScrollNeeded = ko.computed(() => {
            var currentColumnsCount = this.customColumns.columns().length;
            var currentColumnHeight = currentColumnsCount * this.lineHeight;

            if (currentColumnHeight > this.maxTableHeight()) {
                return true;
            }

            return false;
        });
    }

    private generateCompletionBase() {
        this.autoCompleteBase([]);
        this.customColumns.columns().forEach((column: customColumnParams) => this.autoCompleteBase().push(column.binding));

        var moduleSource = "var exports = {}; " + this.customFunctions.functions + "; return exports;";
        var exports = new Function(moduleSource)();
        for (var funcName in exports) {
            this.autoCompleteBase().push(ko.observable<string>(funcName + "()"));
        }
    }

    private regenerateBindingSubscriptions() {
        this.completionSearchSubscriptions.forEach((subscription) => subscription.dispose());
        this.completionSearchSubscriptions = [];
        this.customColumns.columns().forEach((column: customColumnParams, index: number) =>
            this.completionSearchSubscriptions.push(
                column.binding.subscribe(this.searchForCompletions.bind(this))
                )
            );
    }

    private monitorForNewRows() {
        this.customColumns.columns.subscribe((changes: Array<{ index: number; status: string; value: customColumnParams }>) => {
            var somethingRemoved: boolean = false;
            changes.forEach((change) => {
                if (change.status === "added") {
                    this.completionSearchSubscriptions.push(
                        change.value.binding.subscribe(this.searchForCompletions.bind(this))
                        );
                }
                else if (change.status === "deleted") {
                    somethingRemoved = true;
                }

                if (somethingRemoved) {
                    this.regenerateBindingSubscriptions();
                }
            });
        }, null, "arrayChange");
    }

    attached() {
        super.attached();
        this.form = $("#selectColumnsForm");
    }

    cancel() {
        dialog.close(this);
    }

    deactivate() {
        // If we were closed via X button or other dialog dismissal, reject the deletion task since
        // we never started it.
        if (!this.nextTaskStarted) {
            this.nextTask.reject();
        }
    }

    onExit() {
        return this.nextTask.promise();
    }

    changeCurrentColumns() {
        this.nextTaskStarted = true;
        this.nextTask.resolve(this.customColumns);
        dialog.close(this);
    }

    insertNewRow() {
        this.customColumns.columns.push(customColumnParams.empty());

        if (!this.isScrollNeeded()) {
            this.alignBoxVertically();
        }
    }

    deleteRow(row: customColumnParams) {
        this.customColumns.columns.remove(row);

        if (!this.isScrollNeeded()) {
             this.alignBoxVertically();
        }
    }

    moveUp(row: customColumnParams) {
        var i = this.customColumns.columns.indexOf(row);
        if (i >= 1) {
            var array = this.customColumns.columns();
            this.customColumns.columns.splice(i - 1, 2, array[i], array[i - 1]);
        }
    }

    moveDown(row: customColumnParams) {
        var i = this.customColumns.columns.indexOf(row);
        if (i >= 0 && i < this.customColumns.columns().length - 1) {
            var array = this.customColumns.columns();
            this.customColumns.columns.splice(i, 2, array[i + 1], array[i]);
        }
    }

    customScheme(val: boolean) {
        if (this.customColumns.customMode() != val) {
            this.customColumns.customMode(val);
            this.alignBoxVertically();
        }
    }

    private alignBoxVertically() {
        var messageBoxHeight = parseInt($(".messageBox").css('height'), 10);
        var windowHeight = $(window).height();
        var messageBoxMarginTop = parseInt($(".messageBox").css('margin-top'), 10);
        var newTopPercent = Math.floor(((windowHeight - messageBoxHeight) / 2 - messageBoxMarginTop) / windowHeight * 100);
        var newTopPercentString = newTopPercent.toString() + '%';
        $(".modalHost").css('top', newTopPercentString);
    }

    saveAsDefault() {
        if ((<any>this.form[0]).checkValidity() === true) {
            if (this.customColumns.customMode()) {
                var configurationDocument = new document(this.customColumns.toDto());
                new saveDocumentCommand(this.context, configurationDocument, this.database, false).execute()
                    .done(() => this.onConfigSaved())
                    .fail(() => messagePublisher.reportError("Unable to save configuration!"));
            } else {
                new deleteDocumentCommand(this.context, this.database).execute().done(() => this.onConfigSaved())
                    .fail(() => messagePublisher.reportError("Unable to save configuration!"));
            }
        } else {
            messagePublisher.reportWarning('Configuration contains errors. Not saving it.');
        }
    }

    onConfigSaved() {
        messagePublisher.reportSuccess('Configuration saved!');
    }

    generateBindingInputId(index: number) {
        return 'binding-' + index;
    }

    enterKeyPressed():boolean {
        var focusedBindingInput = $("[id ^= 'binding-']:focus");
        if (focusedBindingInput.length) {
            // insert first completion
            if (this.autoCompleteResults().length > 0) {
                this.completeTheWord(this.autoCompleteResults()[0]());
            }
            // prevent submitting the form and closing dialog when accepting completion
            return false;
        }
        return super.enterKeyPressed();
    }

    consumeUpDownArrowKeys(columnParams, event: KeyboardEvent): boolean {
        if (event.keyCode === 38 || event.keyCode === 40) {
            event.preventDefault();
            return false;
        }
        return true;
    }

    searchForCompletions() {
        this.activeInput = $("[id ^= 'binding-']:focus");
        this.autoCompleterSupport.searchForCompletions(this.activeInput);
    }

    completeTheWord(selectedCompletion: string) {
        if (this.activeInput.length > 0) {
            this.autoCompleterSupport.completeTheWord(this.activeInput, selectedCompletion);
        }
    }
}

export = selectColumns;