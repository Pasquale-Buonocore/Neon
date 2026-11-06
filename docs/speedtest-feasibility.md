# Internet speed test feasibility (issue #6)

Technical spike run on 5 November 2026, followed by a download/upload app integration on 6 November 2026. This document records the choice of measurement engine and early tests.

## Current constraints

NEON builds C# source with the .NET Framework 4.x compiler in `build.ps1`. It has no package manager, embedded modern browser, or external runtime dependency. The Monitor network readings are passive interface counters and must remain distinct from active Internet measurements.

## Probes and results

| Probe | Result | Meaning |
| --- | --- | --- |
| Compile and run a program using `System.Net.WebSockets.ClientWebSocket` with NEON's compiler and `System.dll` reference | Passed; `ClientWebSocket.State` was `None` | The current toolchain can build the WebSocket client needed by NDT7. This does not prove the full protocol implementation. |
| Request M-Lab's NDT7 Locate API | Passed; it returned several Italian servers and secure download/upload WebSocket URLs | Server discovery is reachable from the test environment. No measurement transfer was started. |
| Request `https://speed.cloudflare.com` | HTTP 200 for GET | The public page is reachable. This does not test its JavaScript measurement engine. |
| Inspect this checkout for WebView2 assemblies and the machine's standard WebView2 Runtime location | None found | An embedded Cloudflare engine would add a browser SDK/runtime dependency in this environment. This is a local observation, not a claim about every Windows installation. |

The first M-Lab Locate attempt failed because the default sandbox could not resolve the host; the read-only request succeeded with network access. A Cloudflare `HEAD` request returned HTTP 400, while a normal `GET` returned HTTP 200.

The WebSocket compile probe used this minimal program with the compiler and `System.dll` path from `build.ps1`:

```csharp
using System;
using System.Net.WebSockets;
class Probe { static void Main() { using (var ws = new ClientWebSocket()) Console.WriteLine(ws.State); } }
```

It compiled and printed `None`. The discovery probe requested `https://locate.measurementlab.net/v2/nearest/ndt/ndt7`; access-token URLs from that response were not saved in this document.

## Comparison

**M-Lab NDT7:** Uses secure WebSockets and provides a server-discovery API. `ClientWebSocket` is available in the current framework toolchain, so a native C# implementation can preserve NEON's no-package build. Remaining work includes protocol framing, timing, cancellation, error states, server metadata, and a real end-to-end measurement on supported Windows versions. NDT7 is a single-stream, application-level test; its result must be described accordingly. M-Lab collects the client IP and publishes measurement data, so the UI must present its data policy before a user starts a test.

**Cloudflare Speedtest:** Its published measurement engine is a JavaScript module that relies on the browser `PerformanceResourceTiming` API. NEON's WPF `WebBrowser` control hosts the legacy Internet Explorer ActiveX engine. A reliable embedded implementation would use a modern browser control such as WebView2, adding SDK assemblies and a runtime requirement to the current build/distribution path. Opening `speed.cloudflare.com` in the user's browser is simpler, but would leave progress, cancellation, and results outside NEON and would not meet issue #6 as written. Cloudflare states that it collects completed measurement results for aggregate insights.

## First app integration

The project owner chose M-Lab after reviewing the IP/data-publication tradeoff. `src/MlabSpeedTest.cs` uses the Locate API and separate secure NDT7 download and upload WebSockets. `src/SpeedTestWindow.cs` and `src/SpeedGauge.cs` provide the explicit start action, policy link, live tachometer, cancellation, and separate results. The widget menu opens this window; no test starts automatically. Each transfer is capped at about 100 MiB of application data or five seconds, whichever comes first, plus protocol overhead. The normal passive network counters remain separate.

An app-level live test using `NeonMonitor-speedcheck.exe --speed-test artifacts/mlab-integration.json` succeeded after enabling TLS 1.2 for .NET Framework requests. The initial uncapped test downloaded about 642 MB in five seconds; this motivated the cap. A second live test with the cap transferred exactly 100 MiB in about one second and returned a preliminary download rate. This validates the transport and cap on the test machine, not accuracy across networks.

The first full app-level run returned both download and upload throughput and the server's minimum RTT. Early upload attempts with 64 KiB WebSocket messages were rejected by the server; 8 KiB messages, as recommended by the NDT7 specification, succeeded. App-level cancellation checks passed during server discovery, download, and upload. An offline discovery failure and a ten-second discovery timeout were also verified with local test endpoints. Manual UI checks on Windows 10/11, including network interruption during each transfer, remain before closing issue #6.

## Sources

- [NDT7 protocol and Locate API](https://github.com/m-lab/ndt-server/blob/main/spec/ndt7-protocol.md)
- [M-Lab NDT measurements and data collection](https://github.com/m-lab/knowledgebase/blob/main/src/content/articles/test-ndt.md)
- [Cloudflare Speedtest module](https://github.com/cloudflare/speedtest)
- [Microsoft `ClientWebSocket` support](https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets.clientwebsocket?view=netframework-4.5)
- [Microsoft WPF `WebBrowser` implementation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/app-development/wpf-xaml-browser-applications-overview)
- [Microsoft WebView2 distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- [LibreSpeed self-hosting and telemetry settings](https://github.com/librespeed/speedtest/blob/master/doc_docker.md)
