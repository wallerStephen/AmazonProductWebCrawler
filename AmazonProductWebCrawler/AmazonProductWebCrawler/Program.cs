using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using HtmlAgilityPack;
using System.Net;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using AmazonProductWebCrawler;

namespace AmazonWebCrawlerConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Please enter an Amazon address:");
            var address = Console.ReadLine();
            address = address.Remove(address.Length - 1);
            StartCrawlAsync(address);
            Console.Read();
        }

        private static async Task StartCrawlAsync(string address)
        {
            var html = await _RequestHandler(address);

            //Parse html
            var htmlDoc = _HtmlDocToString(html);
            var seeAllUrl = "https://www.amazon.com" + htmlDoc.GetElementbyId("dp-summary-see-all-reviews").GetAttributeValue("href", "");
            var NumberOfReviews = htmlDoc.GetElementbyId("acrCustomerReviewText").InnerText;
            string numbersOnly = Regex.Replace(NumberOfReviews, "[^0-9]", "");


            //Test variables 
            var testThread = ThreadingReviews(seeAllUrl, Int32.Parse(numbersOnly));

        }
        //rewrite http call function return html string 
        private static async Task<string> _RequestHandler(string url)
        {
            //remove after refactoring for preventing using threads uslesslly 
            Console.WriteLine("sent");
            HttpClientHandler handler = new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var urls = url;
            var httpClient = new HttpClient(handler);
            var html = await httpClient.GetStringAsync(url);
            //remove after refactoring for preventing using threads uslesslly 
            Console.WriteLine("returned");
            return html;
        }
        //Turns response to html document 
        private static HtmlDocument _HtmlDocToString(string html)
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            return htmlDoc;
        }
        //Gets Page reviews and returns next url for the chunk
        //Needs to be refactored 
        private static ListAndUrl _PageChunk(string url, int numberOfPages)
        {
            var list = new ListAndUrl();
            var rev = new List<reviews>();
            var html = _RequestHandler(url).Result;
            var htmlDoc = _HtmlDocToString(html);

            //Find the write elements
            var elements = htmlDoc.DocumentNode.Descendants("div")
                .Where(node => node.GetAttributeValue("class", "")
                    .Equals("a-section celwidget")).ToList();

            foreach (var element in elements)
            {
                var review = new reviews()
                {
                    //Add all the attributes for the class 
                    date = element.Descendants("span")
                        .Where(node => node.GetAttributeValue("data-hook", "")
                        .Equals("review-date")).FirstOrDefault().InnerHtml

                };
                rev.Add(review);
            }

            list.listOfReviews = rev;
            //If last page is detected returns null 
            var last = htmlDoc.DocumentNode.Descendants("div")
                .Where(node => node.GetAttributeValue("class", "")
                    .Equals("a-form-actions a-spacing-top-extra-large"))
                .FirstOrDefault().LastChild.Descendants("li")
                .Where(node => node.GetAttributeValue("class", "")
                    .Equals("a-disabled a-last")).Any();

            if (last || numberOfPages == 0)
            {

                list.url = null;
                return list;
            }
            //filler for getting url for the next page 
            var newUrl = htmlDoc.DocumentNode.Descendants("div")
                .Where(node => node.GetAttributeValue("class", "")
                    .Equals("a-form-actions a-spacing-top-extra-large"))
                .FirstOrDefault().LastChild.Descendants("li")
                .Where(node => node.GetAttributeValue("class", "")
                    .Equals("a-last")).First().FirstChild.Attributes["href"].Value;

            //Decode the URL causing issues 
            var decodeString = System.Net.WebUtility.HtmlDecode(newUrl);
            list.url = "https://www.amazon.com" + decodeString;

            return list;
        }
        //Break up and get the number of pages listed within the loop 
        private static List<reviews> _SingleThreadChunk(string start, int loopVar)
        {
            var list = new List<reviews>();
            var reviewsUrls = new ListAndUrl();
            var url = start;
            var i = 0;

            while (i != loopVar && url != null)
            {
                reviewsUrls = _PageChunk(url, loopVar);
                url = reviewsUrls.url;
                list.AddRange(reviewsUrls.listOfReviews);
                i++;
            }
            return list;
        }
        //Start threading of all single thread chunks 
        private static async Task<List<reviews>> ThreadingReviews(string startingPage, int numberOfPost)
        {
            var threads = 20;
            var listTask = new List<Task<List<reviews>>>();
            var listOfReviews = new List<reviews>();
            var numberOfPages = numberOfPost % 10 != 0 ? (numberOfPost / 10) + 1 : numberOfPost / 10;
            var pagesPerThread = numberOfPages % threads != 0 ? (numberOfPages / threads) + 1 : numberOfPages / threads;


            for (int i = 1; i < numberOfPages; i = i + pagesPerThread)
            {
                var startingPageChunk = startingPage + "&pageNumber=" + i;
                listTask.Add(Task.Factory.StartNew(() => _SingleThreadChunk(startingPageChunk, pagesPerThread)));
            }

            await Task.WhenAll(listTask);

            foreach (var v in listTask)
            {
                listOfReviews.AddRange(v.Result);
            }
            //Below can be moved to another function
            var dic = RatingCount(listOfReviews);
            var dict = dic.Keys.OrderByDescending(k => k.Date).ToDictionary(z => z);

            foreach (var d in dic.OrderByDescending(k => k.Key))
            {
                Console.WriteLine(d.Key + string.Concat(Enumerable.Repeat("-", d.Value)));
            }
            return listOfReviews;
        }
        //Converts the list to dic to count number of reviews for the days
        private static Dictionary<DateTime, int> RatingCount(List<reviews> reviews)
        {
            var dic = new Dictionary<DateTime, int>();

            foreach (var elm in reviews)
            {
                var date = DateTime.Parse(elm.date);

                if (!dic.ContainsKey(date))
                {
                    dic.Add(date, 1);
                }
                else
                {
                    dic[date]++;
                }
            }
            return dic;
        }
    }
}
