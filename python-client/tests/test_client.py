
import json
import math
import pytest
import asyncio
from unittest.mock import AsyncMock, MagicMock, patch

import sys
import os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'src'))

from client import (
    MiddlewareClient,
    SensorSimulator,
    WsMessage,
    MessagePriority,
)


class TestWsMessage:
    def test_to_json_contains_required_fields(self):
        msg = WsMessage(type="publish", topic="sensors.temp", payload='{"val": 1}')
        data = json.loads(msg.to_json())
        assert data["type"] == "publish"
        assert data["topic"] == "sensors.temp"
        assert data["source"] == "python-client"
        assert "timestamp" in data

    def test_timestamp_auto_generated(self):
        msg = WsMessage(type="ping", topic="hb", payload="")
        assert msg.timestamp != ""

    def test_priority_default_is_normal(self):
        msg = WsMessage(type="publish", topic="t", payload="p")
        assert msg.priority == MessagePriority.NORMAL


class TestSensorSimulator:
    def setup_method(self):
        self.sim = SensorSimulator("test-sensor")

    def test_temperature_in_expected_range(self):
        for _ in range(50):
            data = self.sim.read_temperature()
            assert -10 <= data["value"] <= 50, f"Unexpected temperature: {data['value']}"

    def test_temperature_has_required_fields(self):
        data = self.sim.read_temperature()
        assert "sensorId" in data
        assert "value" in data
        assert "unit" in data
        assert "alert" in data
        assert data["unit"] == "°C"

    def test_pressure_in_expected_range(self):
        for _ in range(20):
            data = self.sim.read_pressure()
            assert 1000 <= data["value"] <= 1020

    def test_cpu_metrics_range(self):
        data = self.sim.read_cpu_metrics()
        assert 0 <= data["usage_pct"] <= 100
        assert 256 <= data["memory_mb"] <= 2048

    def test_alert_triggered_on_high_temp(self):
        sim = SensorSimulator("test")
        data = sim.read_temperature()
        assert isinstance(data["alert"], bool)


class TestMiddlewareClient:
    def test_on_registers_handler(self):
        client = MiddlewareClient()
        handler = AsyncMock()
        client.on("message", handler)
        assert handler in client._handlers["message"]

    def test_on_multiple_handlers(self):
        client = MiddlewareClient()
        h1, h2 = AsyncMock(), AsyncMock()
        client.on("message", h1)
        client.on("message", h2)
        assert len(client._handlers["message"]) == 2

    @pytest.mark.asyncio
    async def test_send_returns_false_when_not_connected(self):
        client = MiddlewareClient()
        msg = WsMessage(type="publish", topic="t", payload="p")
        result = await client.send(msg)
        assert result is False

    @pytest.mark.asyncio
    async def test_send_increments_stats(self):
        client = MiddlewareClient()
        mock_ws = AsyncMock()
        client._ws = mock_ws

        msg = WsMessage(type="publish", topic="t", payload="p")
        await client.send(msg)

        assert client._stats["sent"] == 1
        mock_ws.send.assert_called_once()

    @pytest.mark.asyncio
    async def test_receive_loop_calls_handlers(self):
        client = MiddlewareClient()
        received = []

        async def handler(data):
            received.append(data)

        client.on("message", handler)

        mock_ws = AsyncMock()
        payload = json.dumps({"type": "message", "topic": "t", "payload": "p"})
        mock_ws.__aiter__ = MagicMock(return_value=iter([payload]))

        await client._receive_loop(mock_ws)

        assert len(received) == 1
        assert received[0]["type"] == "message"
