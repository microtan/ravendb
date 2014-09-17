/// <reference path="../../Scripts/typings/knockout/knockout.d.ts" />

import database = require("models/database");
import indexMergeSuggestion = require("models/indexMergeSuggestion");

class mergedIndexesStorage {
    public static getMergedIndex(db: database, mergedIndexName: string): indexMergeSuggestion {
        var newSuggestion: indexMergeSuggestion = null;

        try {
            if (!!mergedIndexName && mergedIndexName.indexOf(db.mergedIndexLocalStoragePrefix) == 0) {
                var suggestion: suggestionDto = localStorage.getObject(mergedIndexName);
                localStorage.removeItem(mergedIndexName);
                newSuggestion = new indexMergeSuggestion(suggestion);
            }
        }
        catch (e) {
            return null;
        }

        return newSuggestion;
    }

    public static saveMergedIndex(db: database, id: string, suggestion: indexMergeSuggestion) {
        var localStorageName = mergedIndexesStorage.getMergedIndexName(db, id);
        localStorage.setObject(localStorageName, suggestion.toDto());
    }

    public static getMergedIndexName(db: database, id: string) {
        return db.mergedIndexLocalStoragePrefix + '.' + id;
    }
}

export = mergedIndexesStorage;