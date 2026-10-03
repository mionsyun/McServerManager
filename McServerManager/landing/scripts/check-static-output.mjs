import assert from 'node:assert/strict';
import { readFile, readdir, stat, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';

const root=path.resolve('.output/public');
async function htmlFiles(directory) {
  const result=[];
  for(const file of await readdir(directory,{withFileTypes:true})) {
    const name=path.join(directory,file.name);
    if(file.isDirectory()) result.push(...await htmlFiles(name));
    else if(file.name==='index.html') result.push(name);
  }
  return result;
}
const files=[path.join(root,'index.html'),...await htmlFiles(path.join(root,'docs'))];
let schemaCount=0, linkCount=0, imageCount=0;
const errors=[];
for(const file of files) {
  const html=await readFile(file,'utf8');
  for(const [,json] of html.matchAll(/<script\b[^>]*type=["']application\/ld\+json["'][^>]*>([\s\S]*?)<\/script>/gi)) {
    try { JSON.parse(json);schemaCount++; } catch(e) { errors.push({file:path.relative(root,file),error:e.message}); }
  }
  for(const [full,href] of html.matchAll(/<(?:a|img)\b[^>]*\b(?:href|src)=["'](\/[^"']*)["']/gi)) {
    if(href.startsWith('//'))continue;
    const pathname=new URL(href.replace(/&amp;/g,'&'),'https://www.maipilot.jp').pathname;
    const target=path.resolve(root,'.'+decodeURIComponent(pathname));
    if(full.startsWith('<img'))imageCount++;else linkCount++;
    try { assert.ok(target.startsWith(root));await stat(target); } catch {errors.push({file:path.relative(root,file),missing:pathname});}
  }
  assert.equal((html.match(/src="\/analytics\.js"/g)||[]).length,1,`${file}: exactly one shared analytics loader`);
  assert.doesNotMatch(html,/gtag\('config'/,`${file}: no legacy inline GA loader`);
}
const result={html_pages:files.length,valid_json_ld_blocks:schemaCount,internal_link_references:linkCount,local_image_references:imageCount,errors};
await mkdir('test-results',{recursive:true});
await writeFile('test-results/static-checks.json',JSON.stringify(result,null,2)+'\n');
console.log(JSON.stringify(result,null,2));
assert.deepEqual(errors,[]);
