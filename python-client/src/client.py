import asyncio
import json
import logging
import math
import random
import signal
import sys
import time
from dataclasses import dataclass, asdict
from datetime import datetime, timezone
from enum import IntEnum
from typing import Callable, Optional

import websockets
from websockets.exceptions import ConnectionClosed, WebSocketException


logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)-8s] %(name)s: %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger("middleware.client")



class MessagePriority(IntEnum):
    LOW = 0
    NORMAL = 1
    HIGH = 2
    CRITICAL = 3


@dataclass
class WsMessage:
    type: str
    topic: str
    payload: str
    priority: int = MessagePriority.NORMAL
    source: str = "python-client"
    target: Optional[str] = None
    timestamp: str = ""

    def __post_init__(self):
        if not self.timestamp:
            self.timestamp = datetime.now(timezone.utc).isoformat()

    def to_json(self) -> str:
        return json.dumps(asdict(self))



class SensorSimulator:

    def __init__(self, sensor_id: str = "sensor-001"):
        self.sensor_id = sensor_id
        self._tick = 0

    def read_temperature(self) -> dict:
        base = 20 + 5 * math.sin(self._tick * 0.1)
        noise = random.gauss(0, 0.5)
        value = round(base + noise, 2)
        alert = value > 28 or value < 15

        self._tick += 1
        return {
            "sensorId": self.sensor_id,
            "metric": "temperature",
            "value": value,
            "unit": "°C",
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "alert": alert,
        }

    def read_pressure(self) -> dict:
        value = round(random.uniform(1000, 1020), 2)
        return {
            "sensorId": self.sensor_id,
            "metric": "pressure",
            "value": value,
            "unit": "hPa",
            "timestamp": datetime.now(timezone.utc).isoformat(),
        }

    def read_cpu_metrics(self) -> dict:
        return {
            "sensorId": self.sensor_id,
            "metric": "cpu",
            "usage_pct": round(random.uniform(10, 90), 1),
            "memory_mb": random.randint(256, 2048),
            "timestamp": datetime.now(timezone.utc).isoformat(),
        }



class MiddlewareClient:
    def __init__(
        self,
        server_url: str = "ws://localhost:5000/ws",
        client_id: str = "python-01",
        max_retries: int = 10,
    ):
        self.server_url = f"{server_url}?clientId={client_id}"
        self.client_id = client_id
        self.max_retries = max_retries
        self._ws: Optional[websockets.WebSocketClientProtocol] = None
        self._running = False
        self._handlers: dict[str, list[Callable]] = {}
        self.simulator = SensorSimulator(sensor_id=client_id)
        self._stats = {"sent": 0, "received": 0, "errors": 0}


    def on(self, message_type: str, handler: Callable):
        self._handlers.setdefault(message_type, []).append(handler)

    async def send(self, message: WsMessage) -> bool:
        if self._ws is None:
            log.warning("Not connected, cannot send")
            return False
        try:
            await self._ws.send(message.to_json())
            self._stats["sent"] += 1
            log.debug("Sent [%s] on '%s'", message.priority, message.topic)
            return True
        except Exception as e:
            log.error("Send error: %s", e)
            self._stats["errors"] += 1
            return False

    async def run(self):
        self._running = True
        attempt = 0

        while self._running and attempt < self.max_retries:
            try:
                backoff = min(2 ** attempt, 30)
                if attempt > 0:
                    log.info("Reconnecting in %ds (attempt %d/%d)…", backoff, attempt, self.max_retries)
                    await asyncio.sleep(backoff)

                log.info("Connecting to %s", self.server_url)
                async with websockets.connect(
                    self.server_url,
                    ping_interval=20,
                    ping_timeout=10,
                ) as ws:
                    self._ws = ws
                    attempt = 0  # reset on success
                    log.info("✅ Connected as '%s'", self.client_id)
                    await self._session(ws)

            except (ConnectionClosed, WebSocketException) as e:
                log.warning("Connection lost: %s", e)
            except OSError as e:
                log.error("Network error: %s", e)
            except Exception as e:
                log.exception("Unexpected error: %s", e)
            finally:
                self._ws = None
                attempt += 1

        log.info("Client stopped. Stats: %s", self._stats)

    async def stop(self):
        self._running = False
        if self._ws:
            await self._ws.close()


    async def _session(self, ws):
        await asyncio.gather(
            self._receive_loop(ws),
            self._simulation_loop(),
            self._heartbeat_loop(),
        )

    async def _receive_loop(self, ws):
        async for raw in ws:
            try:
                data = json.loads(raw)
                self._stats["received"] += 1
                msg_type = data.get("type", "unknown")
                log.info("← [%s] %s", msg_type, json.dumps(data)[:120])

                for handler in self._handlers.get(msg_type, []):
                    await handler(data)
                for handler in self._handlers.get("*", []):
                    await handler(data)

            except json.JSONDecodeError:
                log.warning("Received non-JSON: %s", raw[:100])

    async def _simulation_loop(self):
        tick = 0
        while self._running and self._ws:
            tick += 1

            temp = self.simulator.read_temperature()
            priority = MessagePriority.CRITICAL if temp["alert"] else MessagePriority.NORMAL
            await self.send(WsMessage(
                type="publish",
                topic="sensors.temperature",
                payload=json.dumps(temp),
                priority=priority,
            ))

            if tick % 5 == 0:
                pressure = self.simulator.read_pressure()
                await self.send(WsMessage(
                    type="publish",
                    topic="sensors.pressure",
                    payload=json.dumps(pressure),
                    priority=MessagePriority.LOW,
                ))

            if tick % 10 == 0:
                cpu = self.simulator.read_cpu_metrics()
                await self.send(WsMessage(
                    type="publish",
                    topic="system.metrics",
                    payload=json.dumps(cpu),
                    priority=MessagePriority.NORMAL,
                ))

            await asyncio.sleep(2)

    async def _heartbeat_loop(self):
        while self._running and self._ws:
            await asyncio.sleep(30)
            await self.send(WsMessage(
                type="ping",
                topic="__heartbeat__",
                payload="",
                priority=MessagePriority.LOW,
            ))



async def main():
    import os

    server_url = os.getenv("MIDDLEWARE_URL", "ws://localhost:5000/ws")
    client_id = os.getenv("CLIENT_ID", f"python-{random.randint(1000, 9999)}")

    client = MiddlewareClient(server_url=server_url, client_id=client_id)

    @client.on("message")
    async def on_message(data):
        log.info("📨 Message received on topic '%s'", data.get("topic"))

    @client.on("pong")
    async def on_pong(data):
        log.debug(" Pong received: %s", data.get("timestamp"))

    @client.on("error")
    async def on_error(data):
        log.error("🚨 Server error: %s", data.get("message"))

    loop = asyncio.get_event_loop()

    def shutdown():
        log.info("Shutting down…")
        asyncio.create_task(client.stop())

    for sig in (signal.SIGINT, signal.SIGTERM):
        loop.add_signal_handler(sig, shutdown)

    await client.run()


if __name__ == "__main__":
    asyncio.run(main())
