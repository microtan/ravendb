﻿import viewModelBase = require("viewmodels/viewModelBase");
import getIndexMergeSuggestionsCommand = require("commands/getIndexMergeSuggestionsCommand");
import database = require("models/database");
import appUrl = require("common/appUrl");
import mergedIndexesStorage = require("common/mergedIndexesStorage");
import indexMergeSuggestion = require("models/indexMergeSuggestion");
import getDatabaseStatsCommand = require("commands/getDatabaseStatsCommand");
import changeSubscription = require('models/changeSubscription');
import shell = require("viewmodels/shell");
import moment = require("moment");
import dialog = require("plugins/dialog");
import optional = require("common/optional");

class indexMergeSuggestions extends viewModelBase {
    
    appUrls: computedAppUrls;
    suggestions = ko.observableArray<indexMergeSuggestion>();
    unmergables = ko.observableArray<{ indexName: string; reason: string; }>();
    idleOrAbandonedIndexes = ko.observableArray<indexStatisticsDto>();
    notUsedForLastWeek = ko.observableArray<indexStatisticsDto>();
    
    constructor() {
        super();
        this.appUrls = appUrl.forCurrentDatabase();
    }

    canActivate(args: any) :any {
        var deferred = $.Deferred();
        this.reload()
            .done(() => deferred.resolve({ can: true }))
            .fail(() => deferred.resolve({ redirect: appUrl.forIndexes(this.activeDatabase()) }));

        return deferred;
    }

    private reload() {
        var fetchIndexMergeSuggestionsTask = this.fetchIndexMergeSuggestions();
        var fetchStatsTask = this.fetchStats();
        return $.when(fetchIndexMergeSuggestionsTask, fetchStatsTask);
    }

    createNotifications(): Array<changeSubscription> {
        return [ shell.currentResourceChangesApi().watchAllIndexes(() => this.fetchIndexMergeSuggestions()) ];
    }

    private fetchStats(): JQueryPromise<databaseStatisticsDto> {
        var db = this.activeDatabase();
        if (db) {
            return new getDatabaseStatsCommand(db)
                .execute()
                .done((result: databaseStatisticsDto) => this.processStatsResults(result));
        }

        return null;
    }

    private processStatsResults(stats: databaseStatisticsDto) {
        this.idleOrAbandonedIndexes([]);
        this.notUsedForLastWeek([]);
        var now = moment();
        var secondsInWeek = 100 * 3600 * 24 * 7;
        stats.Indexes.forEach(indexDto => {
            // we are using contains not equals as priority may contains 
            if (indexDto.Priority.contains("Idle") || indexDto.Priority.contains("Abandoned")) {
                this.idleOrAbandonedIndexes.push(indexDto);
            }

            if (indexDto.LastQueryTimestamp) {
                var lastQueryDate = moment(indexDto.LastQueryTimestamp);
                if (lastQueryDate.isValid()) {
                    var agoInMs = now.diff(lastQueryDate);
                    if (agoInMs > secondsInWeek) {
                        indexDto['LastQueryTimestampText'] = optional.val(indexDto.LastQueryTimestamp).bind(v => v.toHumanizedDate());
                        this.notUsedForLastWeek.push(indexDto);
                    }
                }
            }
        });
    }

    private fetchIndexMergeSuggestions() {
        var deferred = $.Deferred();

        var db = this.activeDatabase();
        new getIndexMergeSuggestionsCommand(db)
            .execute()
            .done((results: indexMergeSuggestionsDto) => {
                var suggestions = results.Suggestions.map((suggestion: suggestionDto) => new indexMergeSuggestion(suggestion));
                this.suggestions(suggestions);

                var unmergables = Object.keys(results.Unmergables).map((value, index) => {
                    return { indexName: value, reason: results.Unmergables[value] }
                });
                this.unmergables(unmergables);
                deferred.resolve();
            })
            .fail(() => deferred.reject());

        return deferred;
    }

    mergeSuggestionIndex(index: string): number {
        return parseInt(index) + 1;
    }

    mergedIndexUrl(id: string) {
        var db: database = this.activeDatabase();
        var mergedIndexName = mergedIndexesStorage.getMergedIndexName(db, id);

        return this.appUrls.editIndex(mergedIndexName);
    }

    saveMergedIndex(id: string, suggestion: indexMergeSuggestion) {
        var db: database = this.activeDatabase();
        mergedIndexesStorage.saveMergedIndex(db, id, suggestion);

        return true;
    }

    deleteIndex(name: string) {
        require(["viewmodels/deleteIndexesConfirm"], deleteIndexesConfirm => {
            var db = this.activeDatabase();
            var deleteViewModel = new deleteIndexesConfirm([name], db);
            deleteViewModel.deleteTask.always(() => this.reload());
            dialog.show(deleteViewModel);
        });
    }

    deleteAllIdleOrAbandoned () {
        require(["viewmodels/deleteIndexesConfirm"], deleteIndexesConfirm => {
            var db = this.activeDatabase();
            var deleteViewModel = new deleteIndexesConfirm(this.idleOrAbandonedIndexes().map(index => index.Name), db, "Delete all idle or abandoned indexes?");
            deleteViewModel.deleteTask.always(() => this.reload());
            dialog.show(deleteViewModel); 
        });
    }

    deleteAllNotUsedForWeek() {
        require(["viewmodels/deleteIndexesConfirm"], deleteIndexesConfirm => {
            var db = this.activeDatabase();
            var deleteViewModel = new deleteIndexesConfirm(this.notUsedForLastWeek().map(index => index.Name), db, "Delete all indexes not used within last week?");
            deleteViewModel.deleteTask.always(() => this.reload());
            dialog.show(deleteViewModel);
        });
    }
}

export = indexMergeSuggestions; 