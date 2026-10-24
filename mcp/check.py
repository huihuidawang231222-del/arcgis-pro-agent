"""MCP protocol harness; live mode calls Pro SDK bridge, without launching another AI."""
import argparse, json, subprocess, sys
from pathlib import Path

parser=argparse.ArgumentParser();parser.add_argument("--pid",type=int);parser.add_argument("--clear",action="store_true");args=parser.parse_args()
process=subprocess.Popen([sys.executable,"-X","utf8",str(Path(__file__).with_name("server.py"))],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding="utf-8")
records=[];counter=0
def rpc(method,params):
    global counter
    counter+=1;process.stdin.write(json.dumps(dict(jsonrpc="2.0",id=counter,method=method,params=params))+"\n");process.stdin.flush()
    reply=json.loads(process.stdout.readline());assert reply["id"]==counter,reply
    assert "error" not in reply,reply
    records.append(dict(method=method,params=params,result=reply["result"]))
    return reply["result"]
def call(name,arguments,fail=False):
    result=rpc("tools/call",dict(name=name,arguments=arguments))
    assert result.get("isError",False)==fail,result
    return result["content"][0]["text"] if fail else json.loads(result["content"][0]["text"])
try:
    rpc("initialize",dict(protocolVersion="2024-11-05",capabilities={},clientInfo=dict(name="local-pro-qa",version="1")))
    process.stdin.write(json.dumps(dict(jsonrpc="2.0",method="notifications/initialized"))+"\n");process.stdin.flush()
    assert len(rpc("tools/list",{})["tools"])==14
    sessions=call("list_sessions",{})["sessions"]
    if args.pid:
        assert any(s["processId"]==args.pid for s in sessions),sessions
        current=call("get_active_map",dict(processId=args.pid));target=dict(processId=args.pid,mapUri=current["mapUri"])
        if args.clear:
            assert call("clear_graphics",target)["status"]=="cleared"
        else:
            plan=dict(summary="Synthetic WGS84 QA point line polygon",drawings=[
                dict(type="point",coordinates=[[113.30,23.13]],color="#FF0000",size=12),
                dict(type="polyline",coordinates=[[113.29,23.12],[113.30,23.13],[113.31,23.12]],color="#0066FF",size=3),
                dict(type="polygon",coordinates=[[113.295,23.135],[113.305,23.135],[113.305,23.145],[113.295,23.135]],color="#66CC66",size=2)])
            result=call("draw_graphics",dict(**target,plan=plan));assert result["count"]==3 and result["persistence"]=="temporary_overlay"
            invalid=dict(summary="invalid",drawings=[dict(type="point",coordinates=[[999,23.13]],color="#FF0000",size=12)])
            call("draw_graphics",dict(**target,plan=invalid),True)
            call("zoom_to_graphics",dict(processId=args.pid,mapUri="invalid-map"),True)
            assert call("zoom_to_graphics",target)["status"]=="zoomed"
    output=Path(__file__).parent.parent/"outputs"/"mcp-qa";output.mkdir(parents=True,exist_ok=True)
    filename="clear.json" if args.clear else "live.json" if args.pid else "offline.json"
    (output/filename).write_text(json.dumps(dict(passed=True,livePro=bool(args.pid),records=records),ensure_ascii=False,indent=2),encoding="utf-8")
    print(f"PASS: {len(records)} protocol calls; livePro={bool(args.pid)}")
finally:
    process.terminate();process.wait(timeout=10)
