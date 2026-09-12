using System;
using System.Collections.Generic;
using GCam.Core.Diagnostics;
using Xunit;

namespace GCam.Core.Tests.Diagnostics
{
    /// <summary>
    /// Covers the policy in <see cref="ErrorHandler"/>: what gets logged, what gets
    /// shown, and what is suppressed.
    /// </summary>
    /// <remarks>
    /// These run headless with no SOLIDWORKS. Note the Debug-build rethrow is guarded
    /// by Debugger.IsAttached, which is false under the test runner - so these tests
    /// exercise the same path in Debug and Release.
    /// </remarks>
    public class ErrorHandlerTests
    {
        private readonly FakeLog _log = new FakeLog();
        private readonly FakePresenter _presenter = new FakePresenter();

        private ErrorHandler CreateHandler() => new ErrorHandler(_log, _presenter);

        [Fact]
        public void UserException_is_shown_as_a_plain_message()
        {
            CreateHandler().Handle(new GCamUserException("Tool will not fit."), "OnCommand");

            Assert.Equal(new[] { "Tool will not fit." }, _presenter.UserErrors);
            Assert.Empty(_presenter.Errors);
        }

        [Fact]
        public void UserException_is_logged_at_Info_not_Error()
        {
            CreateHandler().Handle(new GCamUserException("Nope."), "OnCommand");

            Assert.Contains(_log.Entries, e => e.Level == "Info");
            Assert.DoesNotContain(_log.Entries, e => e.Level == "Error");
        }

        [Fact]
        public void Unexpected_exception_is_logged_at_Error_and_shown_with_detail()
        {
            CreateHandler().Handle(new InvalidOperationException("boom"), "OnCommand");

            Assert.Contains(_log.Entries, e => e.Level == "Error");
            Assert.Single(_presenter.Errors);
            Assert.Empty(_presenter.UserErrors);
        }

        [Fact]
        public void Quiet_suppresses_the_dialog_but_still_logs()
        {
            CreateHandler().Handle(new InvalidOperationException("boom"), "OnCommandEnable", quiet: true);

            Assert.Contains(_log.Entries, e => e.Level == "Error");
            Assert.Empty(_presenter.Errors);
        }

        [Fact]
        public void Repeated_failure_in_the_same_context_is_shown_only_once()
        {
            ErrorHandler handler = CreateHandler();

            for (int i = 0; i < 50; i++)
            {
                handler.Handle(new InvalidOperationException("boom"), "BufferSwapNotify");
            }

            Assert.Single(_presenter.Errors);
            Assert.Single(_log.Entries, e => e.Level == "Error");
        }

        [Fact]
        public void Different_contexts_are_reported_independently()
        {
            ErrorHandler handler = CreateHandler();

            handler.Handle(new InvalidOperationException("boom"), "ContextA");
            handler.Handle(new InvalidOperationException("boom"), "ContextB");

            Assert.Equal(2, _presenter.Errors.Count);
        }

        [Fact]
        public void Null_exception_is_ignored()
        {
            CreateHandler().Handle(null, "OnCommand");

            Assert.Empty(_presenter.Errors);
            Assert.Empty(_presenter.UserErrors);
            Assert.Empty(_log.Entries);
        }

        [Fact]
        public void A_failing_presenter_does_not_replace_the_original_error()
        {
            var handler = new ErrorHandler(_log, new ThrowingPresenter());

            // Must not propagate: we are already inside exception handling.
            handler.Handle(new InvalidOperationException("original"), "OnCommand");

            Assert.Contains(_log.Entries, e => e.Level == "Error" && e.Exception is InvalidOperationException);
            Assert.Contains(_log.Entries, e => e.Level == "Error" && e.Message.Contains("presenter"));
        }

        [Fact]
        public void Null_dependencies_fall_back_to_the_no_op_implementations()
        {
            var handler = new ErrorHandler(null, null);

            handler.Handle(new InvalidOperationException("boom"), "OnCommand");
        }

        private sealed class LogEntry
        {
            public string Level { get; set; }

            public string Message { get; set; }

            public Exception Exception { get; set; }
        }

        private sealed class FakeLog : IGCamLog
        {
            public List<LogEntry> Entries { get; } = new List<LogEntry>();

            public void Debug(string message, params object[] args) => Add("Debug", message, null);

            public void Info(string message, params object[] args) => Add("Info", message, null);

            public void Warn(string message, params object[] args) => Add("Warn", message, null);

            public void Error(Exception ex, string message, params object[] args) => Add("Error", message, ex);

            private void Add(string level, string message, Exception ex) =>
                Entries.Add(new LogEntry { Level = level, Message = message, Exception = ex });
        }

        private sealed class FakePresenter : IErrorPresenter
        {
            public List<Exception> Errors { get; } = new List<Exception>();

            public List<string> UserErrors { get; } = new List<string>();

            public void ShowError(Exception ex, string context) => Errors.Add(ex);

            public void ShowUserError(string message) => UserErrors.Add(message);
        }

        private sealed class ThrowingPresenter : IErrorPresenter
        {
            public void ShowError(Exception ex, string context) => throw new InvalidOperationException("presenter broke");

            public void ShowUserError(string message) => throw new InvalidOperationException("presenter broke");
        }
    }
}
