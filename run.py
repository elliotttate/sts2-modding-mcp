"""Entry point for the STS2 Modding MCP Server."""
import asyncio
from sts2mcp.server import main

if __name__ == "__main__":
    asyncio.run(main())
