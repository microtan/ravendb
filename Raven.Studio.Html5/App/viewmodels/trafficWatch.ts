﻿import app = require("durandal/app");
import viewModelBase = require("viewmodels/viewModelBase");
import watchTrafficConfigDialog = require("viewmodels/watchTrafficConfigDialog");
import trafficWatchClient = require("common/trafficWatchClient");
import getSingleAuthTokenCommand = require("commands/getSingleAuthTokenCommand");
import moment = require("moment");
import fileDownloader = require("common/fileDownloader");
import resource = require("models/resource");

class trafficWatch extends viewModelBase {
    logConfig = ko.observable<{ Resource: resource; ResourceName:string; ResourcePath: string; MaxEntries: number; WatchedResourceMode: string; SingleAuthToken: singleAuthToken }>();
    watchClient: trafficWatchClient;
    isConnected = ko.observable(false);
    recentEntries = ko.observableArray<any>([]);
    now = ko.observable<Moment>();
    updateNowTimeoutHandle = 0;
    selectedLog = ko.observable<logDto>();
    columnWidths: Array<KnockoutObservable<number>>;
    keepDown = ko.observable(false);
    watchedRequests = ko.observable<number>(0);
    averageRequestDuration = ko.observable<string>();
    summedRequestsDuration:number=0;
    minRequestDuration = ko.observable<number>(1000000);
    maxRequestDuration = ko.observable<number>(0);
    startTraceTime = ko.observable<Moment>();
    startTraceTimeHumanized :KnockoutComputed<string>;
    showLogDetails=ko.observable<boolean>(false);
    logRecordsElement:Element;

    constructor() {
        super();
        this.startTraceTimeHumanized = ko.computed(()=> {
            var a = this.now();
            if (!!this.startTraceTime()) {
                return this.parseHumanReadableTimeString(this.startTraceTime().toString(), true, false);
            }
        return "";
    });
    }

    canActivate(args): any {
        return true;
    }
    
    activate(args) {
        var widthUnit = 0.08;
        this.columnWidths = [
            ko.observable<number>(100 * widthUnit),
            ko.observable<number>(100 * widthUnit),
            ko.observable<number>(100 * widthUnit ),
            ko.observable<number>(100 * widthUnit),
            ko.observable<number>(100 * widthUnit * 7),
            ko.observable<number>(100 * widthUnit)
        ];
        this.registerColumnResizing();    
    }

    attached() {
        this.showLogDetails.subscribe(x => {
                $(".logRecords").toggleClass("logRecords-small");
        });
        this.updateCurrentNowTime();
        this.logRecordsElement = document.getElementById("logRecords");
    }

    registerColumnResizing() {
        var resizingColumn = false;
        var startX = 0;
        var startingWidth = 0;
        var columnIndex = 0;

        $(document).on("mousedown.logTableColumnResize", ".column-handle", (e: any) => {
            columnIndex = parseInt($(e.currentTarget).attr("column"));
            startingWidth = this.columnWidths[columnIndex]();
            startX = e.pageX;
            resizingColumn = true;
        });

        $(document).on("mouseup.logTableColumnResize", "", (e: any) => {
            resizingColumn = false;
        });

        $(document).on("mousemove.logTableColumnResize", "", (e: any) => {
            if (resizingColumn) {
                var logsRecordsContainerWidth = $("#logRecordsContainer").width();
                var targetColumnSize = startingWidth + 100 * (e.pageX - startX) / logsRecordsContainerWidth;
                this.columnWidths[columnIndex](targetColumnSize);

                // Stop propagation of the event so the text selection doesn't fire up
                if (e.stopPropagation) e.stopPropagation();
                if (e.preventDefault) e.preventDefault();
                e.cancelBubble = true;
                e.returnValue = false;

                return false;
            }
        });
    }


    configureConnection() {
        var configDialog = new watchTrafficConfigDialog();
        app.showDialog(configDialog);

        configDialog.configurationTask.done((x: any) => {
            this.logConfig(x);
            this.reconnect();
        });
    }

    reconnect() {
        if (!this.watchClient) {
            if (!this.logConfig) {
                app.showMessage("Cannot reconnect, please configure connection properly", "Connection Error");
                return;
            }
            this.connect();
        } else {
            this.disconnect().done(() => {
                this.connect();
            });
        }
    }

    connect() {
        if (!!this.watchClient) {
            this.reconnect();
            return;
        }
        if (!this.logConfig()) {
            this.configureConnection();
            return;
        }

        var tokenDeferred = $.Deferred();

        if (!this.logConfig().SingleAuthToken) {
            new getSingleAuthTokenCommand(this.logConfig().Resource, this.logConfig().WatchedResourceMode == "AdminView")
                .execute()
                .done((tokenObject: singleAuthToken) => {
                    this.logConfig().SingleAuthToken = tokenObject;
                    tokenDeferred.resolve();
                })
                .fail((e) => {
                    app.showMessage("You are not authorized to trace this resource", "Authorization error");
                });
        } else {
            tokenDeferred.resolve();
        }

        tokenDeferred.done(() => {
            this.watchClient = new trafficWatchClient(this.logConfig().ResourcePath, this.logConfig().SingleAuthToken.Token);
            this.watchClient.connect();
            this.watchClient.connectionOpeningTask.done(() => {
                this.isConnected(true);
                this.watchClient.watchTraffic((event: logNotificationDto) => {
                    this.processHttpTraceMessage(event);
                });
                if (!this.startTraceTime()) {
                    this.startTraceTime(this.now());
                }
            });
            this.logConfig().SingleAuthToken = null;
        });
    }
    
    disconnect(): JQueryPromise<any> {
        if (!!this.watchClient) {
            this.watchClient.disconnect();
            return this.watchClient.connectionClosingTask.done(() => {
                this.watchClient = null;
                this.isConnected(false);
            });
        } else {
            app.showMessage("Cannot disconnect, connection does not exist", "Disconnect");
            return $.Deferred().reject();
        }
    }

    deactivate() {
        super.deactivate();
        if (this.isConnected()==true)
            this.disconnect();
    }

    processHttpTraceMessage(e: logNotificationDto) {
        var logObject;
        logObject = {
            Time: this.createHumanReadableTime(e.TimeStamp, false, true),
            Duration: e.ElapsedMilliseconds,
            Resource: e.TenantName,
            Method: e.HttpMethod,
            Url: e.RequestUri,
            CustomInfo: e.CustomInfo,
            TimeStampText: this.createHumanReadableTime(e.TimeStamp, true, false)
        };
        if (this.recentEntries().length == this.logConfig().MaxEntries) {
            this.recentEntries.shift();
        }
        this.recentEntries.push(logObject);

        if (this.keepDown() == true) {
            this.logRecordsElement.scrollTop = this.logRecordsElement.scrollHeight * 1.1;
        }

        this.watchedRequests(this.watchedRequests() + 1);
        this.summedRequestsDuration += e.ElapsedMilliseconds;
        this.averageRequestDuration((this.summedRequestsDuration / this.watchedRequests()).toFixed(2));
        this.minRequestDuration(this.minRequestDuration() > e.ElapsedMilliseconds ? e.ElapsedMilliseconds : this.minRequestDuration());
        this.maxRequestDuration(this.maxRequestDuration() < e.ElapsedMilliseconds ? e.ElapsedMilliseconds : this.maxRequestDuration());
    }


    selectLog(log: logDto) {
        this.selectedLog(log);
        this.showLogDetails(true);
        $(".logRecords").addClass("logRecords-small");
    }

    updateCurrentNowTime() {
        this.now(moment());
        if (this.updateNowTimeoutHandle != 0)
            clearTimeout(this.updateNowTimeoutHandle);
        this.updateNowTimeoutHandle = setTimeout(() => this.updateCurrentNowTime(), 1000);
    }

    createHumanReadableTime(time: string, chainHumanized: boolean= true, chainDateTime: boolean= true): KnockoutComputed<string> {
        if (time) {
            return ko.computed(() => {
                return this.parseHumanReadableTimeString(time, chainHumanized, chainDateTime);
            });
        }

        return ko.computed(() => time);
    }

    parseHumanReadableTimeString(time: string, chainHumanized: boolean= true, chainDateTime: boolean= true)
{
        var dateMoment = moment(time);
        var humanized = "", formattedDateTime = "";
        var agoInMs = dateMoment.diff(this.now());
        if (chainHumanized == true)
            humanized = moment.duration(agoInMs).humanize(true);
        if (chainDateTime == true)
            formattedDateTime = dateMoment.format(" (ddd MMM DD YYYY HH:mm:ss.SS[GMT]ZZ)");
        return humanized + formattedDateTime;
}

    formatLogRecord(logRecord: logNotificationDto) {
        return 'Request #' + logRecord.RequestId.toString().paddingRight(' ', 4) + ' ' + logRecord.HttpMethod.paddingLeft(' ', 7) + ' - ' + logRecord.ElapsedMilliseconds.toString().paddingRight(' ', 5) + ' ms - ' + logRecord.TenantName.paddingLeft(' ', 10) + ' - ' + logRecord.ResponseStatusCode + ' - ' + logRecord.RequestUri;
    }

    resetStats() {
        this.watchedRequests(0);
        this.averageRequestDuration("0");
        this.summedRequestsDuration = 0;
        this.minRequestDuration(1000000);
        this.maxRequestDuration(0);
        this.startTraceTime(null);
    }

    exportTraffic() {
        fileDownloader.downloadAsJson(this.recentEntries(), "traffic.json");
    }

    clearLogs() {
        this.recentEntries.removeAll();
    }

    toggleKeepDown() {
        this.keepDown.toggle();
        if (this.keepDown() == true) {
            this.logRecordsElement.scrollTop = this.logRecordsElement.scrollHeight * 1.1;
        }
    }
}

export =trafficWatch;