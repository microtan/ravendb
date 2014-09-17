import getStatusDebugMetricsCommand = require("commands/getStatusDebugMetricsCommand");
import appUrl = require("common/appUrl");
import database = require("models/database");
import viewModelBase = require("viewmodels/viewModelBase");


class statusDebugMetrics extends viewModelBase {
    data = ko.observable<statusDebugMetricsDto>();
   
    requestPercentiles = ko.computed<any[]>(() => {
        if (this.data()) {
            return this.extractPercentiles(this.data().RequestsDuration.Percentiles);
        }
        return null;
    });

    staleIndexMapsPercentiles = ko.computed<any[]>(() => {
        if (this.data()) {
            return this.extractPercentiles(this.data().StaleIndexMaps.Percentiles);
        }
        return null;
    });

    staleIndexReducesPercentiles = ko.computed<any[]>(() => {
        if (this.data()) {
            return this.extractPercentiles(this.data().StaleIndexReduces.Percentiles);
        }
        return null;
    });

    gauges = ko.computed<any[]>(() => {
        if (this.data()) {
            return $.map(this.data().Gauges, (v, k) => {
                return {
                    key: k,
                    values: $.map(v, (innerValue, innerKey) => {
                        return {
                            key: innerKey,
                            value: innerValue
                        }
                    })
                }
            });

        }
        return null;
    });

    replicationDestinations = ko.computed<string[]>(() => {
        if (this.data()) {
            // sample destinations using ReplicationDurationHistogram
            return $.map(this.data().ReplicationBatchSizeHistogram, (v, key) => key);
        }
        return null;
    });

    extractPercentiles(input) {
        var result = [];
        for (var prop in input) {
            var v = input[prop];
            result.push({ "key": prop, "value": v });
        }
        return result;
    }

    activate(args) {
        super.activate(args);

        this.activeDatabase.subscribe(() => this.fetchStatusDebugMetrics());
        return this.fetchStatusDebugMetrics();
    }

    fetchStatusDebugMetrics(): JQueryPromise<statusDebugMetricsDto> {
        var db = this.activeDatabase();
        if (db) {
            return new getStatusDebugMetricsCommand(db)
                .execute()
                .done((results: statusDebugMetricsDto) => this.data(results));
        }

        return null;
    }
}

export = statusDebugMetrics;