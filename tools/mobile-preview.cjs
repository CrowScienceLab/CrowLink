const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const repo = path.resolve(__dirname, '..');
const version = fs.readFileSync(path.join(repo, 'src/CrowLink.App/CrowLink.App.csproj'), 'utf8').match(/<Version>([^<]+)<\/Version>/)[1];
http.createServer((req,res)=>{
  const source=fs.readFileSync(path.join(repo,'src/CrowLink.App/Services/Mobile/MobileWebAssets.cs'),'utf8');
  const html=source.match(/"""\s*([\s\S]*?)\s*"""/)[1].replace('__CROWLINK_PC_NAME__','CrowLink UI test')
    .replace('__CROWLINK_VERSION__',version)
    .replace('</body>','<script>document.querySelector("#pair").classList.add("is-hidden");</script></body>');
  res.setHeader('Content-Type','text/html;charset=utf-8');res.end(html);
}).listen(45989,'127.0.0.1',()=>console.log('Mobile UI fixture listening on 45989'));
