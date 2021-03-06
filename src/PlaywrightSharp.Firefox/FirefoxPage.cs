using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PlaywrightSharp.Firefox.Helper;
using PlaywrightSharp.Firefox.Input;
using PlaywrightSharp.Firefox.Messaging;
using PlaywrightSharp.Firefox.Protocol;
using PlaywrightSharp.Firefox.Protocol.Network;
using PlaywrightSharp.Firefox.Protocol.Page;
using PlaywrightSharp.Firefox.Protocol.Runtime;
using PlaywrightSharp.Helpers;
using PlaywrightSharp.Input;
using PlaywrightSharp.Messaging;
using FirefoxJsonHelper = PlaywrightSharp.Firefox.Helper.JsonHelper;

namespace PlaywrightSharp.Firefox
{
    /// <inheritdoc cref="IPageDelegate"/>
    internal class FirefoxPage : IPageDelegate
    {
        private const string UtilityWorldName = "__playwright_utility_world__";

        private readonly FirefoxSession _session;
        private readonly IBrowserContext _context;
        private readonly Func<Task<Page>> _openerResolver;
        private readonly ConcurrentDictionary<string, WorkerSession> _workers = new ConcurrentDictionary<string, WorkerSession>();
        private readonly FirefoxNetworkManager _networkManager;

        public FirefoxPage(FirefoxSession session, IBrowserContext context, Func<Task<Page>> openerResolver)
        {
            _session = session;
            _context = context;
            _openerResolver = openerResolver;

            RawKeyboard = new FirefoxRawKeyboard(session);
            RawMouse = new FirefoxRawMouse(session);
            Page = new Page(this, _context);
            _networkManager = new FirefoxNetworkManager(session, Page);

            session.MessageReceived += OnMessageReceived;
            Page.FrameDetached += RemoveContextsForFrame;
        }

        public IRawKeyboard RawKeyboard { get; }

        public IRawMouse RawMouse { get; }

        public ConcurrentDictionary<object, FrameExecutionContext> ContextIdToContext { get; } = new ConcurrentDictionary<object, FrameExecutionContext>();

        internal Page Page { get; }

        public async Task<ElementHandle> AdoptElementHandleAsync(ElementHandle handle, FrameExecutionContext to)
        {
            var result = await _session.SendAsync(new PageAdoptNodeRequest
            {
                FrameId = handle.Context.Frame.Id,
                ObjectId = handle.RemoteObject.ObjectId,
                ExecutionContextId = ((FirefoxExecutionContext)to.Delegate).ExecutionContextId,
            }).ConfigureAwait(false);
            return to.CreateHandle(result.RemoteObject) as ElementHandle;
        }

        public Task ClosePageAsync(bool runBeforeUnload)
            => _session.SendAsync(new PageCloseRequest { RunBeforeUnload = runBeforeUnload });

        public async Task<Quad[][]> GetContentQuadsAsync(ElementHandle elementHandle)
        {
            var result = await _session.SendAsync(new PageGetContentQuadsRequest
            {
                FrameId = elementHandle.Context.Frame.Id,
                ObjectId = elementHandle.RemoteObject.ObjectId,
            }).ConfigureAwait(false);
            return Array.ConvertAll(result.Quads, quad => (Quad[])quad);
        }

        public Task<LayoutMetric> GetLayoutViewportAsync()
            => Page.EvaluateAsync<LayoutMetric>("() => ({ width: innerWidth, height: innerHeight })");

        public bool CanScreenshotOutsideViewport() => true;

        public Task ResetViewportAsync(Viewport viewport) => throw new NotImplementedException();

        public Task SetBackgroundColorAsync(Color? color = null) => throw new NotImplementedException();

        public async Task<byte[]> TakeScreenshotAsync(ScreenshotFormat format, ScreenshotOptions options, Viewport viewport)
        {
            var response = await _session.SendAsync(new PageScreenshotRequest
            {
                MimeType = format == ScreenshotFormat.Jpeg ? ScreenshotMimeType.ImageJpeg : ScreenshotMimeType.ImagePng,
                FullPage = options.FullPage,
                Clip = options.Clip,
            }).ConfigureAwait(false);
            return Convert.FromBase64String(response.Data);
        }

        public bool IsElementHandle(IRemoteObject remoteObject) => ((RemoteObject)remoteObject).Subtype == RemoteObjectSubtype.Node;

        public async Task<GotoResult> NavigateFrameAsync(IFrame frame, string url, string referrer)
        {
            var response = await _session.SendAsync(new PageNavigateRequest
            {
                Url = url,
                Referer = referrer,
                FrameId = frame.Id,
            }).ConfigureAwait(false);
            return new GotoResult { NewDocumentId = response.NavigationId, IsSameDocument = response.NavigationId == null };
        }

        public Task SetViewportAsync(Viewport viewport) => _session.SendAsync(new PageSetViewportRequest
        {
            Viewport = new Protocol.Page.Viewport
            {
                Width = viewport.Width,
                Height = viewport.Height,
                IsMobile = viewport.IsMobile,
                DeviceScaleFactor = viewport.DeviceScaleFactor,
                HasTouch = viewport.IsMobile,
                IsLandscape = viewport.Width > viewport.Height,
            },
        });

        public async Task<Rect> GetBoundingBoxForScreenshotAsync(ElementHandle handle)
        {
            var response = await _session.SendAsync(new PageGetBoundingBoxRequest
            {
                FrameId = handle.Context.Frame.Id,
                ObjectId = handle.RemoteObject.ObjectId,
            }).ConfigureAwait(false);
            return response.BoundingBox;
        }

        public async Task<Rect> GetBoundingBoxAsync(ElementHandle handle)
        {
            var quads = await GetContentQuadsAsync(handle).ConfigureAwait(false);
            if (quads == null || quads.Length == 0)
            {
                return null;
            }

            double minX = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity;
            double maxY = double.NegativeInfinity;
            foreach (var quad in quads)
            {
                foreach (var point in quad)
                {
                    minX = Math.Min(minX, point.X);
                    maxX = Math.Max(maxX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxY = Math.Max(maxY, point.Y);
                }
            }

            return new Rect
            {
                X = minX,
                Y = minY,
                Width = maxX - minX,
                Height = maxY - minY,
            };
        }

        public async Task ExposeBindingAsync(string name, string functionString)
        {
            await _session.SendAsync(new PageAddBindingRequest { Name = name }).ConfigureAwait(false);
            await _session.SendAsync(new PageAddScriptToEvaluateOnNewDocumentRequest
            {
                Script = functionString,
            }).ConfigureAwait(false);
            await Task.WhenAll(Array.ConvertAll(Page.Frames, frame => frame.EvaluateAsync(functionString))).ConfigureAwait(false);
        }

        public Task EvaluateOnNewDocumentAsync(string source)
            => _session.SendAsync(new PageAddScriptToEvaluateOnNewDocumentRequest
            {
                Script = source,
            });

        public async Task<string> GetOwnerFrameAsync(ElementHandle elementHandle)
        {
            var result = await _session.SendAsync(new PageDescribeNodeRequest
            {
                FrameId = elementHandle.Context.Frame.Id,
                ObjectId = elementHandle.RemoteObject.ObjectId,
            }).ConfigureAwait(false);
            return result.OwnerFrameId;
        }

        public async Task<IFrame> GetContentFrameAsync(ElementHandle elementHandle)
        {
            var response = await _session.SendAsync(new PageDescribeNodeRequest
            {
                FrameId = elementHandle.Context.Frame.Id,
                ObjectId = elementHandle.RemoteObject.ObjectId,
            }).ConfigureAwait(false);
            if (string.IsNullOrEmpty(response.ContentFrameId))
            {
                return null;
            }

            return Page.FrameManager.Frames[response.ContentFrameId];
        }

        public Task SetExtraHttpHeadersAsync(IDictionary<string, string> headers)
            => _session.SendAsync(new NetworkSetExtraHTTPHeadersRequest
            {
                Headers = headers.Select(pair => new HTTPHeader
                {
                    Name = pair.Key,
                    Value = pair.Value,
                }).ToArray(),
            });

        public Task ReloadAsync() => _session.SendAsync(new PageReloadRequest { FrameId = Page.MainFrame.Id });

        internal async Task InitializeAsync()
        {
            var tasks = new List<Task>
            {
               _session.SendAsync(new RuntimeEnableRequest()),
               _session.SendAsync(new NetworkEnableRequest()),
               _session.SendAsync(new PageEnableRequest()),
            };

            if (_context.Options != null)
            {
                var options = _context.Options;

                if (options.Viewport != null)
                {
                    tasks.Add(SetViewportAsync(options.Viewport));
                }

                if (options.BypassCSP)
                {
                    tasks.Add(_session.SendAsync(new PageSetBypassCSPRequest { Enabled = true }));
                }

                if (!options.JavaScriptEnabled)
                {
                    tasks.Add(_session.SendAsync(new PageSetJavascriptEnabledRequest { Enabled = false }));
                }

                if (options.UserAgent != null)
                {
                    tasks.Add(_session.SendAsync(new PageSetUserAgentRequest { UserAgent = options.UserAgent }));
                }
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            await EnsureIsolatedWorldAsync(UtilityWorldName).ConfigureAwait(false);
        }

        internal void DidClose() => Page.DidClose();

        private Task EnsureIsolatedWorldAsync(string name)
            => _session.SendAsync(new PageAddScriptToEvaluateOnNewDocumentRequest
            {
                Script = string.Empty,
                WorldName = name,
            });

        private void OnMessageReceived(object sender, IFirefoxEvent e)
        {
            switch (e)
            {
                case PageEventFiredFirefoxEvent pageEventFired:
                    OnEventFired(pageEventFired);
                    break;
                case PageFrameAttachedFirefoxEvent pageFrameAttached:
                    OnFrameAttached(pageFrameAttached);
                    break;
                case PageFrameDetachedFirefoxEvent pageFrameDetached:
                    OnFrameDetached(pageFrameDetached);
                    break;
                case PageNavigationAbortedFirefoxEvent pageNavigationAborted:
                    break;
                case PageNavigationCommittedFirefoxEvent pageNavigationCommitted:
                    OnNavigationCommitted(pageNavigationCommitted);
                    break;
                case PageNavigationStartedFirefoxEvent pageNavigationStarted:
                    break;
                case PageSameDocumentNavigationFirefoxEvent pageSameDocumentNavigation:
                    OnSameDocumentNavigation(pageSameDocumentNavigation);
                    break;
                case RuntimeExecutionContextCreatedFirefoxEvent runtimeExecutionContextCreated:
                    OnExecutionContextCreated(runtimeExecutionContextCreated);
                    break;
                case RuntimeExecutionContextDestroyedFirefoxEvent runtimeExecutionContextDestroyed:
                    OnExecutionContextDestroyed(runtimeExecutionContextDestroyed);
                    break;
                case PageUncaughtErrorFirefoxEvent pageUncaughtError:
                    break;
                case RuntimeConsoleFirefoxEvent runtimeConsole:
                    OnConsole(runtimeConsole);
                    break;
                case PageDialogOpenedFirefoxEvent pageDialogOpened:
                    break;
                case PageBindingCalledFirefoxEvent pageBindingCalled:
                    OnBindingCalled(pageBindingCalled);
                    break;
                case PageFileChooserOpenedFirefoxEvent pageFileChooserOpened:
                    break;
                case PageWorkerCreatedFirefoxEvent pageWorkerCreated:
                    OnWorkerCreated(pageWorkerCreated);
                    break;
                case PageWorkerDestroyedFirefoxEvent pageWorkerDestroyed:
                    OnWorkerDestroyed(pageWorkerDestroyed);
                    break;
                case PageDispatchMessageFromWorkerFirefoxEvent pageDispatchMessageFromWorker:
                    OnDispatchMessageFromWorker(pageDispatchMessageFromWorker);
                    break;
            }
        }

        private void OnEventFired(PageEventFiredFirefoxEvent pageEventFired)
        {
            if (pageEventFired.Name == EventFiredName.Load)
            {
                Page.FrameManager.FrameLifecycleEvent(pageEventFired.FrameId, WaitUntilNavigation.Load);
            }

            if (pageEventFired.Name == EventFiredName.DOMContentLoaded)
            {
                Page.FrameManager.FrameLifecycleEvent(pageEventFired.FrameId, WaitUntilNavigation.DOMContentLoaded);
            }
        }

        private void OnFrameAttached(PageFrameAttachedFirefoxEvent pageFrameAttached)
            => Page.FrameManager.FrameAttached(pageFrameAttached.FrameId, pageFrameAttached.ParentFrameId);

        private void OnFrameDetached(PageFrameDetachedFirefoxEvent pageFrameDetached)
            => Page.FrameManager.FrameDetached(pageFrameDetached.FrameId);

        private void OnNavigationCommitted(PageNavigationCommittedFirefoxEvent pageNavigationCommitted)
        {
            foreach (var pair in _workers)
            {
                if (pair.Key == pageNavigationCommitted.FrameId)
                {
                    OnWorkerDestroyed(new PageWorkerDestroyedFirefoxEvent { WorkerId = pair.Key });
                }
            }

            Page.FrameManager.FrameCommittedNewDocumentNavigation(pageNavigationCommitted.FrameId, pageNavigationCommitted.Url, pageNavigationCommitted.Name ?? string.Empty, pageNavigationCommitted.NavigationId ?? string.Empty, false);
        }

        private void OnSameDocumentNavigation(PageSameDocumentNavigationFirefoxEvent pageSameDocumentNavigation)
            => Page.FrameManager.FrameCommittedSameDocumentNavigation(pageSameDocumentNavigation.FrameId, pageSameDocumentNavigation.Url);

        private void OnExecutionContextCreated(RuntimeExecutionContextCreatedFirefoxEvent runtimeExecutionContextCreated)
        {
            var auxData = runtimeExecutionContextCreated.AuxData != null
                ? ((JsonElement)runtimeExecutionContextCreated.AuxData).ToObject<AuxData>()
                : null;
            if (auxData?.FrameId != null && Page.FrameManager.Frames.TryGetValue(auxData.FrameId, out var frame))
            {
                var firefoxDelegate = new FirefoxExecutionContext(_session, runtimeExecutionContextCreated.ExecutionContextId);
                var context = new FrameExecutionContext(firefoxDelegate, frame);
                if (auxData.Name == UtilityWorldName)
                {
                    frame.ContextCreated(ContextType.Utility, context);
                }
                else if (string.IsNullOrEmpty(auxData.Name))
                {
                    frame.ContextCreated(ContextType.Main, context);
                }

                ContextIdToContext[runtimeExecutionContextCreated.ExecutionContextId] = context;
            }
        }

        private void OnExecutionContextDestroyed(RuntimeExecutionContextDestroyedFirefoxEvent runtimeExecutionContextDestroyed)
        {
            if (ContextIdToContext.TryRemove(runtimeExecutionContextDestroyed.ExecutionContextId, out var context))
            {
                context.Frame.ContextDestroyed(context);
            }
        }

        private void RemoveContextsForFrame(object sender, FrameEventArgs e)
        {
            foreach (var pair in ContextIdToContext)
            {
                if (pair.Value.Frame == e.Frame)
                {
                    ContextIdToContext.TryRemove(pair.Key, out _);
                }
            }
        }

        private void OnBindingCalled(PageBindingCalledFirefoxEvent pageBindingCalled)
        {
            var context = ContextIdToContext[pageBindingCalled.ExecutionContextId];
            _ = Page.OnBindingCalledAsync(pageBindingCalled.Payload.ToString(), context);
        }

        private void OnConsole(RuntimeConsoleFirefoxEvent runtimeConsole)
        {
            var context = ContextIdToContext[runtimeConsole.ExecutionContextId];

            var type = runtimeConsole.GetConsoleType();
            var location = runtimeConsole.ToConsoleMessageLocation();

            Page.AddConsoleMessage(type, Array.ConvertAll(runtimeConsole.Args, arg => context.CreateHandle(arg)), location);
        }

        private void OnWorkerCreated(PageWorkerCreatedFirefoxEvent pageWorkerCreated)
        {
            string workerId = pageWorkerCreated.WorkerId;
            var worker = new Worker(pageWorkerCreated.Url);
            FirefoxSession tempWorkerSession = null;
            var workerSession = new FirefoxSession(_session.Connection, "worker", workerId, async (id, request) =>
            {
                try
                {
                    await _session.SendAsync(new PageSendMessageToWorkerRequest
                    {
                        FrameId = pageWorkerCreated.FrameId,
                        WorkerId = workerId,
                        Message = new ConnectionRequest
                        {
                            Id = id,
                            Method = request.Command,
                            Params = request,
                        }.ToJson(FirefoxJsonHelper.DefaultJsonSerializerOptions),
                    }).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    tempWorkerSession.OnMessage(new ConnectionResponse
                    {
                        Id = id,
                        Method = string.Empty,
                        Error = new ConnectionError
                        {
                            Message = e.Message,
                        },
                    });
                }
            });
            tempWorkerSession = workerSession;

            _workers[workerId] = new WorkerSession(pageWorkerCreated.FrameId, workerSession);
            Page.AddWorker(workerId, worker);
            void HandleRuntimeExecutionContextCreated(object sender, IFirefoxEvent e)
            {
                if (e is RuntimeExecutionContextCreatedFirefoxEvent runtimeExecutionContextCreated)
                {
                    worker.CreateExecutionContext(new FirefoxExecutionContext(workerSession, runtimeExecutionContextCreated.ExecutionContextId));
                    workerSession.MessageReceived -= HandleRuntimeExecutionContextCreated;
                }
            }

            workerSession.MessageReceived += HandleRuntimeExecutionContextCreated;
            workerSession.MessageReceived += (sender, e) =>
            {
                if (e is RuntimeConsoleFirefoxEvent runtimeConsole)
                {
                    var context = worker.ExistingExecutionContext;
                    var type = runtimeConsole.GetConsoleType();
                    var location = runtimeConsole.ToConsoleMessageLocation();

                    Page.AddConsoleMessage(type, Array.ConvertAll(runtimeConsole.Args, arg => context.CreateHandle(arg)), location);
                }
            };
        }

        private void OnWorkerDestroyed(PageWorkerDestroyedFirefoxEvent pageWorkerDestroyed)
        {
            string workerId = pageWorkerDestroyed.WorkerId;
            if (_workers.TryRemove(workerId, out var worker))
            {
                worker.Session.OnClosed(pageWorkerDestroyed.InternalName);
                Page.RemoveWorker(workerId);
            }
        }

        private void OnDispatchMessageFromWorker(PageDispatchMessageFromWorkerFirefoxEvent pageDispatchMessageFromWorker)
        {
            throw new NotImplementedException();
        }

        private class WorkerSession
        {
            public WorkerSession(string frameId, FirefoxSession session) => (FrameId, Session) = (frameId, session);

            public string FrameId { get; }

            public FirefoxSession Session { get; }
        }

        private class AuxData
        {
            public string FrameId { get; set; }

            public string Name { get; set; }
        }
    }
}
