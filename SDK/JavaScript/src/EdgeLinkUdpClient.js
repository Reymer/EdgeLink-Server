"use strict";

const dgram        = require("dgram");
const EventEmitter = require("events");

/**
 * UDP receiver — binds to a local port and emits "message" for each packet.
 *
 * Events:
 *   "message" (string)
 *   "error"   (Error)
 */
class EdgeLinkUdpClient extends EventEmitter {
    /**
     * @param {number} localPort
     */
    constructor(localPort) {
        super();
        this.localPort = localPort;
        this._socket   = null;
        this.isRunning = false;
    }

    start() {
        this._socket = dgram.createSocket("udp4");

        this._socket.on("message", (buf) => {
            const msg = buf.toString("utf8").trim();
            if (msg) this.emit("message", msg);
        });

        this._socket.on("error", (err) => this.emit("error", err));

        this._socket.bind(this.localPort, () => {
            this.isRunning = true;
        });
    }

    stop() {
        if (this._socket) {
            this._socket.close();
            this._socket = null;
        }
        this.isRunning = false;
    }
}

/**
 * UDP sender — send-only, no local port binding required.
 */
class EdgeLinkUdpSender {
    constructor() {
        this._socket = dgram.createSocket("udp4");
    }

    /**
     * @param {string} host
     * @param {number} port
     * @param {string} message
     * @returns {Promise<void>}
     */
    send(host, port, message) {
        return new Promise((resolve, reject) => {
            const buf = Buffer.from(message, "utf8");
            this._socket.send(buf, 0, buf.length, port, host, (err) => {
                if (err) reject(err);
                else resolve();
            });
        });
    }

    close() {
        this._socket.close();
    }
}

module.exports = { EdgeLinkUdpClient, EdgeLinkUdpSender };
