import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc';

const app = readFileSync(new URL('../app.vue', import.meta.url),'utf8');
const translationLiteral = app.slice(app.indexOf('const translations = ') + 'const translations = '.length, app.indexOf('} as const;', app.indexOf('const translations'))+1);
const translations=vm.runInNewContext('('+translationLiteral+')');

test('JA/EN have identical top-level keys and all template keys resolve',()=>{
  assert.deepEqual(Object.keys(translations.ja).sort(),Object.keys(translations.en).sort());
  for(const [,key] of parse(app).descriptor.template.content.matchAll(/\bt\.(\w+)/g)) {
    assert.ok(key in translations.ja,`JA missing ${key}`); assert.ok(key in translations.en,`EN missing ${key}`);
  }
});
test('version comes from the updater manifest, and support price is metadata only',()=>{
  const config=readFileSync(new URL('../nuxt.config.ts',import.meta.url),'utf8');
  assert.match(config,/appVersion: process.env.NUXT_PUBLIC_APP_VERSION \|\| updateManifest.version/);
  assert.match(app,/price: "0"/); assert.match(app,/price: "100"/); assert.doesNotMatch(app,/price: "980"/);
  assert.match(app,/https:\/\/maipilot.booth.pm\/items\/8118402/);
});
for(const filename of ['app.vue','components/PcHandoff.vue','components/ProductScreenshot.vue']) {
  test(`Vue script and template compile: ${filename}`,()=>{
    const source=readFileSync(new URL('../'+filename,import.meta.url),'utf8');
    const {descriptor,errors}=parse(source,{filename}); assert.deepEqual(errors,[]);
    const script=compileScript(descriptor,{id:filename});
    const template=compileTemplate({source:descriptor.template.content,filename,id:filename,compilerOptions:{bindingMetadata:script.bindings}});
    assert.deepEqual(template.errors,[]);
  });
}
test('all primary installer paths hand mobile users off to Windows',()=>{
  assert.equal((app.match(/:data-event="isMobileDevice \? 'pc_handoff_open' : 'download_click'"/g)||[]).length,2);
  assert.match(app,/:data-event="isMobileDevice && option.id === 'local' \? 'pc_handoff_open' : downloadOptionEvents\[option.id\]"/);
});
