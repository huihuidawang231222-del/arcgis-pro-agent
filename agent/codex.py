"""Use the installed, logged-in Codex engine with this local MCP bridge only."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys
import time
import uuid
from run import ROOT, SYSTEM

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--pid',required=True,type=int)
    parser.add_argument('goal',nargs='?')
    args=parser.parse_args()
    codex=shutil.which('codex')
    if not codex:
        parser.error('找不到Codex CLI，请先安装并登录。')
    goal=args.goal or input('你希望制作什么地图？\n> ').strip()
    if not goal:
        parser.error('目标不能为空。')
    out=ROOT/'outputs/agent'/('codex-'+time.strftime('%Y%m%d-%H%M%S')+'-'+uuid.uuid4().hex[:6])
    out.mkdir(parents=True)
    instructions=SYSTEM.replace('requestId由宿主生成，无需自己编造UUID。','requestId必须是有效UUID；同一提交重试复用该UUID。').replace('完成或需要澄清时调用finish_task；status为completed/needs_input/partial，说明真实成果及限制。export_map未成功时不得把制图目标标completed。','完成或需要澄清时直接报告真实结果、文件路径、限制；没有真实导出回执不得声称完成。')
    prompt=instructions+'\n仅操作Pro进程 '+str(args.pid)+'。这是制图任务，不是修改代码任务。不要改任何代码或配置，不启动其他Agent，不使用ArcPy或Shell代替GIS工具；所有GIS操作只调用arcgis-pro MCP。\n用户目标：'+goal
    # Retain normal Codex authentication; no token extraction or global config edits.
    command=[codex,'exec','--ignore-user-config','--skip-git-repo-check','--sandbox','read-only','-C',str(ROOT),'--json','-o',str(out/'answer.md'),
             '-c','mcp_servers.arcgis-pro.command='+json.dumps(sys.executable),
             '-c','mcp_servers.arcgis-pro.args='+json.dumps(['-X','utf8',str(ROOT/'mcp/server.py')]),
             '-c','mcp_servers.arcgis-pro.tool_timeout_sec=200','-']
    (out/'goal.txt').write_text(goal,encoding='utf-8')
    print('GIS_AGENT_RUN '+str(out),flush=True)
    with (out/'events.jsonl').open('w',encoding='utf-8') as events,(out/'diagnostics.log').open('w',encoding='utf-8') as errors:
        process=subprocess.Popen(command,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=errors,text=True,encoding='utf-8',creationflags=subprocess.CREATE_NO_WINDOW if sys.platform=='win32' else 0)
        process.stdin.write(prompt)
        process.stdin.close()
        try:
            for line in process.stdout:
                events.write(line);events.flush()
                event=json.loads(line)
                item=event.get('item',{})
                if item.get('type')=='mcp_tool_call':
                    print(item.get('status','')+' '+item.get('tool',''),flush=True)
                elif event.get('type')=='error':
                    print(event.get('message','引擎错误'),flush=True)
            code=process.wait()
        except KeyboardInterrupt:
            process.terminate();code=130
    print('引擎退出码：'+str(code)+'；记录：'+str(out),flush=True)
    if (out/'answer.md').is_file():
        print((out/'answer.md').read_text(encoding='utf-8'),flush=True)
    return code

if __name__=='__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.exit(main())
