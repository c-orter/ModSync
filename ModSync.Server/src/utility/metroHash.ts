import { readFileSync } from "node:fs";
import { join } from "node:path";


const heap = new Array(128).fill(undefined);

heap.push(undefined, null, true, false);

function getObject(idx: number) { return heap[idx]; }

let heap_next = heap.length;

function dropObject(idx: number) {
	if (idx < 132) return;
	heap[idx] = heap_next;
	heap_next = idx;
}

function takeObject(idx: number) {
	const ret = getObject(idx);
	dropObject(idx);
	return ret;
}

function addHeapObject(obj: Uint8Array) {
	if (heap_next === heap.length) heap.push(heap.length + 1);
	const idx = heap_next;
	heap_next = heap[idx];

	heap[idx] = obj;
	return idx;
}

let cachedUint8ArrayMemory0: Uint8Array | null = null;

function getUint8ArrayMemory0() {
	if (cachedUint8ArrayMemory0 === null || cachedUint8ArrayMemory0.byteLength === 0) {
		cachedUint8ArrayMemory0 = new Uint8Array(wasm.memory.buffer);
	}
	return cachedUint8ArrayMemory0;
}

let WASM_VECTOR_LEN = 0;

function passArray8ToWasm0(arg: Uint8Array, malloc: (size: number, count: number) => number) {
	const ptr = malloc(arg.length, 1) >>> 0;
	getUint8ArrayMemory0().set(arg, ptr);
	WASM_VECTOR_LEN = arg.length;
	return ptr;
}

/**
 * @param {Uint8Array} data
 * @returns {Uint8Array}
 */
export function metrohash128(data: Uint8Array): Uint8Array {
	const ptr0 = passArray8ToWasm0(data, wasm.__wbindgen_export_0);
	const len0 = WASM_VECTOR_LEN;
	const ret = wasm.metrohash128(ptr0, len0);
	return takeObject(ret);
}

const bytes = readFileSync(join(__dirname, "metrohash.wasm"));

const wasmModule = new WebAssembly.Module(bytes);
const wasmInstance = new WebAssembly.Instance(wasmModule, {
	"__wbindgen_placeholder__": {
		__wbindgen_object_drop_ref: (arg0: number) => {
			takeObject(arg0);
		},

		__wbg_buffer_609cc3eee51ed158: (arg0: number) => {
			const ret = getObject(arg0).buffer;
			return addHeapObject(ret);
		},

		__wbg_newwithbyteoffsetandlength_d97e637ebe145a9a: (arg0: number, arg1: number, arg2: number) => {
			const ret = new Uint8Array(getObject(arg0), arg1 >>> 0, arg2 >>> 0);
			return addHeapObject(ret);
		},

		__wbg_new_a12002a7f91c75be: (arg0: number) => {
			const ret = new Uint8Array(getObject(arg0));
			return addHeapObject(ret);
		},

		__wbindgen_memory: () => {
			const ret = wasm.memory;
			return addHeapObject(ret);
		},
	},
});

// biome-ignore lint/suspicious/noExplicitAny: WASM go brrrr...
const wasm: any = wasmInstance.exports;
module.exports.__wasm = wasm;
