"""Publish a verified Unity WebGL build to this game's GitHub Pages branch."""
import argparse,json,subprocess
from pathlib import Path
import requests

ROOT=Path(__file__).resolve().parents[1]
OUTPUT=ROOT/'Build'/'WebGL'
REPO='Notard/white-fox'
def git(*args,cwd=ROOT,**kwargs):
    return subprocess.run(['git',*args],cwd=cwd,check=True,text=True,capture_output=True,**kwargs).stdout.strip()
def api():
    credentials=git('credential','fill',input='protocol=https\nhost=github.com\n\n')
    values=dict(line.split('=',1)for line in credentials.splitlines()if '='in line)
    if not values.get('password'):raise RuntimeError('GitHub credentials unavailable')
    session=requests.Session()
    session.headers.update({'Authorization':'Bearer '+values['password'],'Accept':'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28'})
    return session
parser=argparse.ArgumentParser();parser.add_argument('--status',action='store_true');args=parser.parse_args()
session=api();url=f'https://api.github.com/repos/{REPO}/pages'
if not args.status:
    if not (OUTPUT/'index.html').is_file():raise RuntimeError('WebGL index.html not found')
    for file in OUTPUT.rglob('*'):
        if file.is_file()and '.git' not in file.parts and file.stat().st_size>95*1024*1024:
            raise RuntimeError(f'Build file too large for GitHub: {file.name}')
    (OUTPUT/'.nojekyll').touch()
    if not (OUTPUT/'.git').exists():
        git('init','-b','gh-pages',cwd=OUTPUT)
        git('remote','add','origin',f'https://github.com/{REPO}.git',cwd=OUTPUT)
        for key in ['user.name','user.email']:
            value=git('config','--get',key)
            git('config',key,value,cwd=OUTPUT)
    git('add','.',cwd=OUTPUT)
    if git('diff','--cached','--name-only',cwd=OUTPUT):
        git('commit','-m','Deploy White Fox WebGL build',cwd=OUTPUT)
    git('push','origin','HEAD:gh-pages',cwd=OUTPUT)
    current=session.get(url,timeout=30)
    if current.status_code==404:
        result=session.post(url,json={'source':{'branch':'gh-pages','path':'/'},'build_type':'legacy'},timeout=30)
    else:
        current.raise_for_status()
        result=session.put(url,json={'source':{'branch':'gh-pages','path':'/'},'build_type':'legacy'},timeout=30)
    result.raise_for_status()
    print('Published branch:',git('rev-parse','HEAD',cwd=OUTPUT))
pages=session.get(url,timeout=30);pages.raise_for_status();data=pages.json()
print(json.dumps({'url':data.get('html_url'),'status':data.get('status'),'source':data.get('source')},ensure_ascii=False))
build=session.get(url+'/builds/latest',timeout=30)
if build.ok:
    b=build.json();print(json.dumps({'build_status':b.get('status'),'error':b.get('error')},ensure_ascii=False))
