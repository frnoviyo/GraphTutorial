using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace GraphTutorial.Models
{
    public class UpdateEvent
    {
        [Required]
        public string Id { get; set; }
        [Required]
        public string Subject { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        [DataType(DataType.MultilineText)]
        public string Body { get; set; }
        [RegularExpression(@"((\w+([-+.']\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*)*([;])*)*",
          ErrorMessage = "Please enter one or more email addresses separated by a semi-colon (;)")]
        public string Attendees { get; set; }
    }
}
