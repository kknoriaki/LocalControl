export type Session = {csrf:string;permissions:string[];desktop:boolean;expiresAt:string};
export type Audio = {id:string;label:string;kind:'output'|'microphone'|'session';volume:number;muted:boolean;isDefault:boolean};
export type Snapshot = {revision:number;at:string;machine:{name:string;cpuPercent:number|null;memoryTotalBytes:number;memoryUsedBytes:number;uptimeSeconds:number;disks:{label:string;totalBytes:number;freeBytes:number}[]};audio:Audio[];warnings:string[]};
export type App = {id:string;label:string;source:string;running?:boolean|null;favorite?:boolean;category?:string;pids?:number[];publisher?:string|null};
export type Device = {id:string;label:string;permissions:string[];approvedAt:string};
export type Pending = {id:string;label:string;code:string;peer:string;expiresAt:string};
export type Invitation = {url:string;expiresAt:string};
export type Network = {url:string|null;addresses:string[];port:number};
