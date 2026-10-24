"""A per-run MCP endpoint pinned to the Pro process explicitly selected in the UI."""
import argparse
import server
parser=argparse.ArgumentParser();parser.add_argument('--process-id',required=True,type=int);args=parser.parse_args()
original_sessions=server.sessions
original_bridge=server.bridge
server.sessions=lambda:[s for s in original_sessions() if s['processId']==args.process_id]
def scoped_bridge(action,arguments):
    if arguments.get('processId')!=args.process_id:
        raise ValueError('当前运行只允许操作用户明确选择的Pro实例。')
    return original_bridge(action,arguments)
server.bridge=scoped_bridge
server.main()
