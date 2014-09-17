﻿ /// <reference path="../../Scripts/typings/jquery/jquery.d.ts" />
/// <reference path="../../Scripts/typings/knockout/knockout.d.ts" />

import resource = require('models/resource');
import appUrl = require('common/appUrl');
import changeSubscription = require('models/changeSubscription');
import changesCallback = require('common/changesCallback');
import commandBase = require('commands/commandBase');
import folder = require("models/filesystem/folder");
import getSingleAuthTokenCommand = require("commands/getSingleAuthTokenCommand");
import shell = require("viewmodels/shell");
import changesApi = require("common/changesApi");
import idGenerator = require("common/idGenerator");

class trafficWatchClient {
    
    public connectionOpeningTask: JQueryDeferred<any>;
    public connectionClosingTask: JQueryDeferred<any>;
    private webSocket: WebSocket;
    static isServerSupportingWebSockets: boolean = true;
    private eventSource: EventSource;
    private readyStateOpen = 1;
    private eventsId:string;
    private isCleanClose: boolean = false;
    private normalClosureCode = 1000;
    private normalClosureMessage = "CLOSE_NORMAL";
    static messageWasShownOnce: boolean = false;
    private successfullyConnectedOnce: boolean = false;
    private sentMessages = [];
    private commandBase = new commandBase();
    private adminLogsHandlers = ko.observableArray<changesCallback<logNotificationDto>>();

    constructor(private resourcePath: string, private token:string) {
        this.connectionOpeningTask = $.Deferred();
        this.connectionClosingTask = $.Deferred();
        this.eventsId = idGenerator.generateId();
    }

    public connect() {
        var connectionString = 'singleUseAuthToken=' + this.token + '&id=' + this.eventsId;
        if ("WebSocket" in window && changesApi.isServerSupportingWebSockets) {
            this.connectWebSocket(connectionString);
        } else if ("EventSource" in window) {
            this.connectEventSource(connectionString);
        } else {
            //The browser doesn't support nor websocket nor eventsource
            //or we are in IE10 or IE11 and the server doesn't support WebSockets.
            //Anyway, at this point a warning message was already shown. 
            this.connectionOpeningTask.reject();
        }
    }

    private connectWebSocket(connectionString: string) {
        var connectionOpened: boolean = false;

        this.webSocket = new WebSocket('ws://' + window.location.host + this.resourcePath + '/traffic-watch/websocket?' + connectionString);

        this.webSocket.onmessage = (e) => this.onMessage(e);
        this.webSocket.onerror = (e) => {
            if (connectionOpened == false) {
                this.connectionOpeningTask.reject();
            } else {
                this.connectionClosingTask.resolve({Error:e});
            }
        };
        this.webSocket.onclose = (e: CloseEvent) => {
                this.connectionClosingTask.resolve();
        }
        this.webSocket.onopen = () => {
            console.log("Connected to WebSockets HTTP Trace for " + ((!!this.resourcePath) ? (this.resourcePath) : "admin"));
            this.successfullyConnectedOnce = true;
            connectionOpened = true;
            this.connectionOpeningTask.resolve();
        }
    }

    private connectEventSource(connectionString: string) {
        var connectionOpened: boolean = false;

        this.eventSource = new EventSource(this.resourcePath + '/traffic-watch/events?' + connectionString);

        this.eventSource.onmessage = (e) => this.onMessage(e);
        this.eventSource.onerror = (e) => {
            if (connectionOpened == false) {
                this.connectionOpeningTask.reject();
            } else {
                this.eventSource.close();
                this.connectionClosingTask.resolve(e);
            }
        };
        this.eventSource.onopen = () => {
            console.log("Connected to EventSource HTTP Trace for " + ((!!this.resourcePath) ? (this.resourcePath) : "admin"));
            this.successfullyConnectedOnce = true;
            connectionOpened = true;
            this.connectionOpeningTask.resolve();
        }
    }
    
    private fireEvents<T>(events: Array<any>, param: T, filter: (T) => boolean) {
        for (var i = 0; i < events.length; i++) {
            if (filter(param)) {
                events[i].fire(param);
            }
        }
    }

    private onMessage(e: any) {
        var eventDto: changesApiEventDto = JSON.parse(e.data);
        var type = eventDto.Type;
        var value = eventDto.Value;

        if (type !== "Heartbeat") { // ignore heartbeat
            if (type === "LogNotification") {
                this.fireEvents(this.adminLogsHandlers(), value, (e) => true);
            }
            else {
                console.log("Unhandled Changes API notification type: " + type);
            }
        }
    }

    watchTraffic(onChange: (e: logNotificationDto) => void) {
        var callback = new changesCallback<logNotificationDto>(onChange);
        this.adminLogsHandlers.push(callback);
        return new changeSubscription(() => {
            this.adminLogsHandlers.remove(callback);
        });
    }
    
    disconnect() {
        this.connectionOpeningTask.done(() => {
            if (this.webSocket && this.webSocket.readyState == this.readyStateOpen){
                console.log("Disconnecting from WebSocket HTTP Trace for " + ((!!this.resourcePath) ? (this.resourcePath) : "admin"));
                this.isCleanClose = true;
                this.webSocket.close(this.normalClosureCode, this.normalClosureMessage);
            }
            else if (this.eventSource && this.eventSource.readyState == this.readyStateOpen) {
                console.log("Disconnecting from EventSource HTTP Trace for " + ((!!this.resourcePath) ? (this.resourcePath) : "admin"));
                this.isCleanClose = true;
                this.eventSource.close();
                this.connectionClosingTask.resolve();
            }
        });
    }
}

export = trafficWatchClient;