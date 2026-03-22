"""Allow running the MCP server via `python -m sts2mcp`."""
import asyncio
from .server import main

asyncio.run(main())
