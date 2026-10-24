"""Use the installed DeepSeek Harness and its managed account credentials via MCP."""
import argparse,json,os,subprocess,sys,time,uuid,winreg
from pathlib import Path
from run import ROOT,SYSTEM

def installation():
    for hive in (winreg.HKEY_CURRENT_USER,winreg.HKEY_LOCAL_MACHINE):
        try:
            with winreg.OpenKey(hive,r'Software\Microsoft\Windows\CurrentVersion\Uninstall') as base:
                for i in range(winreg.QueryInfoKey(base)[0]):
                    try:
                        with winreg.OpenKey(base,winreg.EnumKey(base,i)) as key:
                            if 'DeepSeek Harness' not in winreg.QueryValueEx(key,'DisplayName')[0]:continue
                            directory=Path(winreg.QueryValueEx(key,'InstallLocation')[0])
                            if (directory/'DeepSeek Harness.exe').is_file():return directory
                    except OSError:continue
        except OSError:continue
    raise RuntimeError('未找到DeepSeek Harness，请先安装客户端。')

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--pid',required=True,type=int);parser.add_argument('goal',nargs='?');args=parser.parse_args()
    install=installation();goal=args.goal or input('你希望制作什么地图？\n> ').strip()
    if not goal:parser.error('目标不能为空。')
    out=ROOT/'outputs/agent'/('deepseek-'+time.strftime('%Y%m%d-%H%M%S')+'-'+uuid.uuid4().hex[:6]);out.mkdir(parents=True)
    # Use normal managed authentication. Never read or copy stored grant/token values.
    patch=[{'id':'agent-default-model','config':{'provider':'deepseek-account','model':'deepseek-flash','reasoningEffort':'high'}},
           *[{'id':i,'disabled':False} for i in ('credentials','authorization','deepseek-account')],
           {'insert':[{'id':'arcgis-pro-mcp','name':'@deepseek-ai/dsh-mcp-client','config':{'serverName':'arcgis-pro','transport':'stdio','command':sys.executable,'args':['-X','utf8',str(ROOT/'mcp/scoped_server.py'),'--process-id',str(args.pid)],'cwd':str(ROOT),'toolCallTimeoutMs':200000,'failOnStartupError':True}}]}]
    overlay=out/'mcp.patch.yml';overlay.write_text(json.dumps(patch,ensure_ascii=False,indent=2),encoding='utf-8')
    instructions=SYSTEM.replace('requestId由宿主生成，无需自己编造UUID。','requestId必须是UUID，同一提交重试复用该ID。').replace('完成或需要澄清时调用finish_task；status为completed/needs_input/partial，说明真实成果及限制。export_map未成功时不得把制图目标标completed。','结束时直接用中文报告真实结果、限制与文件路径；没有实际导出回执不得声称成图完成。')
    prompt=instructions+'\n只操作Pro进程'+str(args.pid)+'。仅使用arcgis-pro MCP，不使用文件修改、命令、子Agent或其他工具代替GIS操作。\n用户目标：'+goal
    command=[str(install/'DeepSeek Harness.exe'),'--expose-internals',str(install/'resources/app.asar/dsh/node_modules/@deepseek-ai/dsh-desktop-host/lib/cli.js'),'--profile','headless','--patch',str(overlay),'--json','-']
    env=dict(os.environ,ELECTRON_RUN_AS_NODE='1')
    (out/'goal.txt').write_text(goal,encoding='utf-8');print('GIS_AGENT_RUN '+str(out),flush=True)
    calls={};final='';error=None
    with (out/'harness-events.jsonl').open('w',encoding='utf-8') as raw,(out/'events.jsonl').open('w',encoding='utf-8') as normalized,(out/'diagnostics.log').open('w',encoding='utf-8') as diagnostics:
        process=subprocess.Popen(command,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=diagnostics,text=True,encoding='utf-8',env=env,cwd=ROOT,creationflags=subprocess.CREATE_NO_WINDOW)
        process.stdin.write(prompt);process.stdin.close()
        try:
            for line in process.stdout:
                raw.write(line);raw.flush()
                try:e=json.loads(line)
                except ValueError:continue
                kind=e.get('type')
                if kind=='tool_call':
                    calls[e['callId']]=e
                    print('in_progress '+e.get('tool','').removeprefix('mcp__arcgis-pro__'),flush=True)
                elif kind=='tool_result':
                    call=calls.get(e.get('callId'),{});name=call.get('tool','').removeprefix('mcp__arcgis-pro__');value=e.get('result')
                    if not isinstance(value,str):value=json.dumps(value,ensure_ascii=False)
                    item=dict(type='mcp_tool_call',server='arcgis-pro' if call.get('tool','').startswith('mcp__arcgis-pro__') else 'other',tool=name,arguments=call.get('input',{}),result=dict(content=[dict(type='text',text=value)]),status='completed' if e.get('status')=='completed' else 'failed')
                    normalized.write(json.dumps(dict(type='item.completed',item=item),ensure_ascii=False)+'\n');normalized.flush()
                    print(item['status']+' '+name,flush=True)
                elif kind=='final':final=e.get('text','')
                elif kind=='status' and e.get('phase')=='turn_end':
                    reason=e.get('reason',{})
                    if reason.get('kind')=='error':error=reason.get('error',{});print('Harness错误：'+str(error.get('code',''))+' '+str(error.get('message','')),flush=True)
                    if reason.get('kind')=='completed':normalized.write(json.dumps(dict(type='turn.completed'))+'\n');normalized.flush()
            code=process.wait()
        except KeyboardInterrupt:process.terminate();code=130
    (out/'answer.md').write_text(final,encoding='utf-8');(out/'engine-status.json').write_text(json.dumps(dict(exitCode=code,error=error,responded=bool(calls) or bool(final),model='deepseek-account/deepseek-flash'),ensure_ascii=False,indent=2),encoding='utf-8')
    print(final or 'Harness未返回最终回复，请查看运行记录。',flush=True)
    print('引擎退出码：'+str(code)+'；记录：'+str(out),flush=True)
    return code
if __name__=='__main__':
    sys.stdout.reconfigure(encoding='utf-8');sys.exit(main())
