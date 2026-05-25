import {log} from 'console';

export function onPrepare() {
    log("Script is being prepared");
}

export function onStart() {
    log("Script has started");
}

export function onUpdate() {
    log("Script is updating");
}

export function onDestroy() {
    log("Script is being destroyed");
}