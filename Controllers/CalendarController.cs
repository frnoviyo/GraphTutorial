using GraphTutorial.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Microsoft.Graph;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TimeZoneConverter;
using System.Linq;

namespace GraphTutorial.Controllers
{
    public class CalendarController : Controller
    {
        private readonly GraphServiceClient _graphClient;
        private readonly ILogger<HomeController> _logger;

        public CalendarController(
            GraphServiceClient graphClient,
            ILogger<HomeController> logger)
        {
            _graphClient = graphClient;
            _logger = logger;
        }

        private async Task<IList<Event>> GetUserWeekCalendar(DateTime startOfWeekUtc)
        {
            // Configure a calendar view for the current week
            var endOfWeekUtc = startOfWeekUtc.AddDays(7);

            var viewOptions = new List<QueryOption>
                {
                    new QueryOption("startDateTime", startOfWeekUtc.ToString("o")),
                    new QueryOption("endDateTime", endOfWeekUtc.ToString("o"))
                };

            var events = await _graphClient.Me
                .CalendarView
                .Request(viewOptions)
                // Send user time zone in request so date/time in
                // response will be in preferred time zone
                .Header("Prefer", $"outlook.timezone=\"{User.GetUserGraphTimeZone()}\"")
                // Get max 50 per request
                .Top(50)
                // Only return fields app will use
                .Select(e => new
                {
                    e.Subject,
                    e.Organizer,
                    e.Start,
                    e.End
                })
                // Order results chronologically
                .OrderBy("start/dateTime")
                .GetAsync();

            IList<Event> allEvents;
            // Handle case where there are more than 50
            if (events.NextPageRequest != null)
            {
                allEvents = new List<Event>();
                // Create a page iterator to iterate over subsequent pages
                // of results. Build a list from the results
                var pageIterator = PageIterator<Event>.CreatePageIterator(
                    _graphClient, events,
                    (e) => {
                        allEvents.Add(e);
                        return true;
                    }
                );
                await pageIterator.IterateAsync();
            }
            else
            {
                // If only one page, just use the result
                allEvents = events.CurrentPage;
            }

            return allEvents;
        }

        private static DateTime GetUtcStartOfWeekInTimeZone(DateTime today, TimeZoneInfo timeZone)
        {
            // Assumes Sunday as first day of week
            int diff = System.DayOfWeek.Sunday - today.DayOfWeek;

            // create date as unspecified kind
            var unspecifiedStart = DateTime.SpecifyKind(today.AddDays(diff), DateTimeKind.Unspecified);

            // convert to UTC
            return TimeZoneInfo.ConvertTimeToUtc(unspecifiedStart, timeZone);
        }

        // Minimum permission scope needed for this view
        [AuthorizeForScopes(Scopes = new[] { "Calendars.Read" })]
        public async Task<IActionResult> Index()
        {
            try
            {
                var userTimeZone = TZConvert.GetTimeZoneInfo(
                    User.GetUserGraphTimeZone());
                var startOfWeekUtc = CalendarController.GetUtcStartOfWeekInTimeZone(
                    DateTime.Today, userTimeZone);

                var events = await GetUserWeekCalendar(startOfWeekUtc);

                // Convert UTC start of week to user's time zone for
                // proper display
                var startOfWeekInTz = TimeZoneInfo.ConvertTimeFromUtc(startOfWeekUtc, userTimeZone);
                var model = new CalendarViewModel(startOfWeekInTz, events);

                return View(model);
            }
            catch (ServiceException ex)
            {
                if (ex.InnerException is MicrosoftIdentityWebChallengeUserException)
                {
                    throw;
                }

                return View(new CalendarViewModel())
                    .WithError("Error getting calendar view", ex.Message);
            }
        }

        // Minimum permission scope needed for this view
        [AuthorizeForScopes(Scopes = new[] { "Calendars.ReadWrite" })]
        public IActionResult New()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeForScopes(Scopes = new[] { "Calendars.ReadWrite" })]
        public async Task<IActionResult> New([Bind("Subject,Attendees,Start,End,Body")] NewEvent newEvent)
        {
            var timeZone = User.GetUserGraphTimeZone();

            // Create a Graph event with the required fields
            var graphEvent = new Event
            {
                Subject = newEvent.Subject,
                Start = new DateTimeTimeZone
                {
                    DateTime = newEvent.Start.ToString("o"),
                    // Use the user's time zone
                    TimeZone = timeZone
                },
                End = new DateTimeTimeZone
                {
                    DateTime = newEvent.End.ToString("o"),
                    // Use the user's time zone
                    TimeZone = timeZone
                }
            };

            // Add body if present
            if (!string.IsNullOrEmpty(newEvent.Body))
            {
                graphEvent.Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = newEvent.Body
                };
            }

            // Add attendees if present
            if (!string.IsNullOrEmpty(newEvent.Attendees))
            {
                var attendees =
                    newEvent.Attendees.Split(';', StringSplitOptions.RemoveEmptyEntries);

                if (attendees.Length > 0)
                {
                    var attendeeList = new List<Attendee>();
                    foreach (var attendee in attendees)
                    {
                        attendeeList.Add(new Attendee
                        {
                            EmailAddress = new EmailAddress
                            {
                                Address = attendee
                            },
                            Type = AttendeeType.Required
                        });
                    }

                    graphEvent.Attendees = attendeeList;
                }
            }

            try
            {
                // Add the event
                await _graphClient.Me.Events
                    .Request()
                    .AddAsync(graphEvent);

                // Redirect to the calendar view with a success message
                return RedirectToAction("Index").WithSuccess("Event created");
            }
            catch (ServiceException ex)
            {
                // Redirect to the calendar view with an error message
                return RedirectToAction("Index")
                    .WithError("Error creating event", ex.Error.Message);
            }
        }

        // Minimum permission scope needed for this view
        [AuthorizeForScopes(Scopes = new[] { "Calendars.ReadWrite" })]
        public async Task<IActionResult> Update(string id)
        {
            //ViewData["UpdateEvent"] = new UpdateEvent();
            
            var myEvent = await _graphClient.Me.Events[id].Request().GetAsync();

            UpdateEvent viewEvent = new UpdateEvent();
            viewEvent.Id = id;
            viewEvent.Subject = myEvent.Subject;
            var attendees = myEvent.Attendees.ToList();
            if (attendees.Count > 0)
            {
                string emails = String.Empty;
                foreach (var attendee in attendees)
                {
                    emails += attendee.EmailAddress.Address+";";
                   
                }
                viewEvent.Attendees = emails.TrimEnd(';');
            }
            
            viewEvent.Start = DateTime.Parse(myEvent.Start.DateTime);
            viewEvent.End = DateTime.Parse(myEvent.End.DateTime);
            viewEvent.Body = myEvent.BodyPreview;
            ViewData["GetEvent"] = viewEvent;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeForScopes(Scopes = new[] { "Calendars.ReadWrite" })]
        public async Task<IActionResult> Update([Bind("Id,Subject,Attendees,Start,End,Body")] UpdateEvent updateEvent)
        {
            var timeZone = User.GetUserGraphTimeZone();

            // Create a Graph event with the required fields
            var graphEvent = new Event
            {
                Subject = updateEvent.Subject,
                Start = new DateTimeTimeZone
                {
                    DateTime = updateEvent.Start.ToString("o"),
                    // Use the user's time zone
                    TimeZone = timeZone
                },
                End = new DateTimeTimeZone
                {
                    DateTime = updateEvent.End.ToString("o"),
                    // Use the user's time zone
                    TimeZone = timeZone
                }
            };

            // Add body if present
            if (!string.IsNullOrEmpty(updateEvent.Body))
            {
                graphEvent.Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = updateEvent.Body
                };
            }

            // Add attendees if present
            if (!string.IsNullOrEmpty(updateEvent.Attendees))
            {
                var attendees =
                    updateEvent.Attendees.Split(';', StringSplitOptions.RemoveEmptyEntries);

                if (attendees.Length > 0)
                {
                    var attendeeList = new List<Attendee>();
                    foreach (var attendee in attendees)
                    {
                        attendeeList.Add(new Attendee
                        {
                            EmailAddress = new EmailAddress
                            {
                                Address = attendee
                            },
                            Type = AttendeeType.Required
                        });
                    }

                    graphEvent.Attendees = attendeeList;
                }
            }

            try
            {
                // Update the event
                await _graphClient.Me.Events[updateEvent.Id]
                    .Request()
                    .UpdateAsync(graphEvent);

                // Redirect to the calendar view with a success message
                return RedirectToAction("Index").WithSuccess("Event updated");
            }
            catch (ServiceException ex)
            {
                // Redirect to the calendar view with an error message
                return RedirectToAction("Index")
                    .WithError("Error updating event", ex.Error.Message);
            }
        }

        public async Task<IActionResult> Delete(string id)
        {
            await _graphClient.Me.Events[id].Request().DeleteAsync();
            return RedirectToAction("Index").WithSuccess("Event Deleted");
        }
    }

}