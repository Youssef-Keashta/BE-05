using System;
using System.Collections.Generic;
using System.Text;

namespace BE_05
{
    internal class Book
    {
        public string? Title { get; set; }
        public string ProductUrl { get; set; } = "";
        public string? PriceText { get; set; }
        public string? AvailabilityText { get; set; }
        public string? RatingText { get; set; }
        public string? Description { get; set; }
        public string SourcePage { get; set; } = "";
        public string FetchedAt { get; set; } = "";
    }
}
