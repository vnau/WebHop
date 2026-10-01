using WebHop.CLI;

namespace WebHop.Tests.Cli
{
    public class SessionTests
    {
        private readonly Session session = new();

        [Fact]
        public void There_are_no_percentiles_before_the_first_request()
        {
            Assert.Null(session.Percentiles());
        }

        [Fact]
        public void Percentiles_are_computed_over_request_durations()
        {
            foreach (var ms in new[] { 100, 10, 90, 20, 80, 30, 70, 40, 60, 50 })
                Complete(ms);

            var (p50, p90) = session.Percentiles()!.Value;

            Assert.Equal(50, p50);
            Assert.Equal(90, p90);
        }

        [Fact]
        public void Recent_requests_are_listed_newest_first()
        {
            Complete(1, "/first");
            Complete(1, "/second");
            Complete(1, "/third");

            Assert.Equal(["/third", "/second"], session.RecentRequests(2).Select(r => r.Path));
        }

        [Fact]
        public void Totals_and_requests_in_flight_are_counted()
        {
            session.BeginRequest();
            session.BeginRequest();
            Assert.Equal(2, session.InFlight);

            session.EndRequest(Entry(5, "/"));

            Assert.Equal(1, session.InFlight);
            Assert.Equal(1, session.Total);
        }

        [Fact]
        public void Every_change_is_visible_to_the_screen_and_to_listeners()
        {
            var completed = new List<RequestEntry>();
            session.RequestCompleted += completed.Add;
            var before = session.Version;

            Complete(5);
            session.ReportError("boom");

            Assert.True(session.Version > before);
            Assert.Single(completed);
            Assert.Equal("boom", session.LastError);
        }

        private void Complete(int milliseconds, string path = "/")
        {
            session.BeginRequest();
            session.EndRequest(Entry(milliseconds, path));
        }

        private static RequestEntry Entry(int milliseconds, string path) =>
            new(DateTime.Now, "GET", path, 200, TimeSpan.FromMilliseconds(milliseconds), null);
    }
}
