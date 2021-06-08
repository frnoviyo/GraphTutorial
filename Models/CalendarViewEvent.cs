using Microsoft.Graph;
using System;
using System.ComponentModel.DataAnnotations;

namespace GraphTutorial.Models
{
    public class CalendarViewEvent
    {
        public string Id { get; private set; }
        public string Subject { get; private set; }
        public string Organizer { get; private set; }
        public DateTime Start { get; private set; }
        public DateTime End { get; private set; }

        public CalendarViewEvent(Event graphEvent)
        {
            Subject = graphEvent.Subject;
            Organizer = graphEvent.Organizer.EmailAddress.Name;
            Start = DateTime.Parse(graphEvent.Start.DateTime);
            End = DateTime.Parse(graphEvent.End.DateTime);
            Id = graphEvent.Id;
        }
    }
}