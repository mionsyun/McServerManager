import { defineCommand } from "citty";

//#region ../nuxi/src/commands/module/index.ts
var module_default = defineCommand({
	meta: {
		name: "module",
		description: "Manage Nuxt modules"
	},
	args: {},
	subCommands: {
		add: () => import("./add-CbHKQ2Kq.mjs").then((r) => r.default || r),
		search: () => import("./search-CKbK1-IC.mjs").then((r) => r.default || r)
	}
});

//#endregion
export { module_default as default };