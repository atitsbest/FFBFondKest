using CsvHelper;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/*
	Die generelle Steuerpflicht der Anteilscheinveräußerung ist bereits auf alle Anteilscheine anzuwenden, 
	die ab dem 1. Jänner 2011 angeschafft worden sind.

	Der automatische KESt-Abzug auf Kursgewinne erfolgt seit 1. April 2012 und wird nur für realisierte Kursgewinne aus (Wertpapier-) Neubestand abgezogen.

	Das sind:

		* Aktien und Fondsanteile, die ab dem 1. Jänner 2011 erworben wurden und ab dem 1. April 2012 veräußert werden und
		* Forderungswertpapiere und Derivate, die ab dem 1. April 2012 erworben wurden und wieder veräußert werden

	http://www.boerse-live.at/eBusiness/blive_template1/212251493524270627-215486471565447122_277155062893956005-277155062893956005-NA-NA-NA-NA-NA.html
*/

namespace FFB
{
    class Transaction {
        public string Type { get; set; }
        public DateTime Buchungsdatum { get; set; }
        public string ISIN { get; set; }
        public decimal Anteile { get; set; }
        public decimal Abrechnungspreis { get; set; }
        public decimal Ruecknamepreis { get; set; }
        public string Currency { get; set; }
        public decimal Ausgabeaufschlage { get; set; }
        public decimal AbrechnungsbetragInEUR { get; set; }

        public decimal Devisenkurs { get; set; }
        public decimal RuecknamepreisInEUR { get { return Ruecknamepreis / Devisenkurs; } }
        public decimal AbrechnungspreisInEUR { get { return Abrechnungspreis / Devisenkurs; } }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var ts = new List<Transaction>();

            // Alles deutsch.
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var config = new CsvHelper.Configuration.CsvConfiguration() {
                Delimiter = ";",
                CultureInfo = new System.Globalization.CultureInfo("de-DE")
            };

            var csv = new CsvReader(File.OpenText("report-20131125.csv"), config);
            while (csv.Read()) {
                var type = csv.GetField<string>("Transaktion").ToLower();

                if (type == "erträgnis ohne wiederanlage" ||
                    type.Contains("zugang") ||
                    type.Contains("merge") ||
                    type.Contains("übertrag")) continue;

				ts.Add(new Transaction() { 
					Type = type,
					Buchungsdatum = csv.GetField<DateTime>("Buchungsdatum"),
					ISIN = csv.GetField<string>("ISIN"),
					Anteile = csv.GetField<decimal>("Anteile"),
					Abrechnungspreis = csv.GetField<decimal>("Abrechnungspreis"),
					Ruecknamepreis = csv.GetField<decimal>("Rücknahmepreis"),
					Currency = csv.GetField<string>("Fondswährung"),
					Devisenkurs = csv.GetField<decimal>("Devisenkurs"),
					Ausgabeaufschlage = csv.GetField<decimal>("Ausgabeaufschlag in EUR"),
					AbrechnungsbetragInEUR = csv.GetField<decimal>("Abrechnungsbetrag in EUR")
				});
            }

			// Liste mit allen Fonds (ISIN) ermitteln.
            var isins = ts.Select(t => t.ISIN).Distinct();

			// Pro Fond einen eigenen Stack mit chronologischen Buchungen mit je 1 Anteil.
            var fonds = ts
                .OrderBy(t => t.Buchungsdatum)
                .GroupBy(t => t.ISIN)
                .ToDictionary(g => g.Key, g => g.ToList());

			// Header schreiben.
			Console.WriteLine("Buchungsdatum\tISIN\tTyp\tAnteile\tRücknamepreis €\tAbrechnungspreis €\tAbrechnungsbetrag €\tGewinn\tKeSt Gewinn");

            //var isins = new string[] { "LU0255798109", "DE0008475005", "DE0009751750", "LU0055114457", "LU0212963259" };
            foreach (var isin in isins) {
                CalculateYearlyWinFor(fonds[isin]); 
            }

        }

		/// <summary>
		/// Berechnet den Jährlichen gewinn für den übergebenen Fonds.
		/// </summary>
		/// <param name="isin"></param>
		/// <returns></returns>
        public static Dictionary<int, decimal> CalculateYearlyWinFor(IEnumerable<Transaction> transactions)
        {
            var gewinne = new Dictionary<int, decimal>();
            var kestGewinne = new Dictionary<int, decimal>();
            var depot = new Queue<Transaction>();
			var kestFreiDatum = new DateTime(2011, 1, 1);

			// Zur sicherheit sortieren wir die Transaktionen nochmal nach Buchungsdatum.
            var ts = transactions.OrderBy(t => t.Buchungsdatum);
            
            foreach (var t in ts) {
                Console.Write("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}", 
                    t.Buchungsdatum.ToShortDateString(), t.ISIN, t.Type, t.Anteile, t.RuecknamepreisInEUR, t.AbrechnungspreisInEUR, t.AbrechnungsbetragInEUR);
                
                if (t.Type.Contains("verkauf") || t.Type.Contains("entgeltbelastung")) {
                    var anteile = t.Anteile;
                    var gewinn = 0.0M;
                    var kestGewinn = 0.0M;

                    while (anteile > 0.0M) {
                        var last = depot.Peek();
                        if (last.Anteile > anteile) {
                            //Console.Write("{0}*{1} ", anteile, last.RuecknamepreisInEUR);
                            // Gewinn berechnen.
                            var kpreis = anteile * last.RuecknamepreisInEUR;
                            var vpreis = anteile * t.RuecknamepreisInEUR;
                            gewinn += vpreis - kpreis;
                            if (last.Buchungsdatum >= kestFreiDatum) {
                                kestGewinn += vpreis - kpreis;
                            }

                            // Anteile aktualisieren.
                            last.Anteile -= anteile;
                            anteile = 0;
                        }
                        else {
                            //Console.Write("{0}*{1} ", last.Anteile, last.RuecknamepreisInEUR);
                            // Gewinn berechnen.
                            var kpreis = last.Anteile * last.RuecknamepreisInEUR;
                            var vpreis = last.Anteile * t.RuecknamepreisInEUR;
                            gewinn += vpreis - kpreis;
                            if (last.Buchungsdatum >= kestFreiDatum) {
                                kestGewinn += vpreis - kpreis;
                            }

                            depot.Dequeue();
                            anteile -= last.Anteile;
                        }
                    }

                    // Zum jährlichen Gewinn hinzufügen.
                    if (t.Type.Contains("entgeltbelastung")) {
						Console.Write("\t0");
						Console.WriteLine("\t0");
                    }
                    else {
                        _mehrGewinn(gewinne, t, gewinn);
                        _mehrGewinn(kestGewinne, t, kestGewinn);

                        Console.Write("\t{0}", gewinn);
                        Console.WriteLine("\t{0}", kestGewinn);
                    }
                }
                else if (t.Type.Contains("kauf") || t.Type == "erträgnis") {
					Console.Write("\t0");
					Console.WriteLine("\t0");
                    depot.Enqueue(t);
                }
            }

            return gewinne;
        }

        private static void _mehrGewinn(Dictionary<int, decimal> gewinne, Transaction t, decimal gewinn)
        {
            if (gewinne.ContainsKey(t.Buchungsdatum.Year)) gewinne[t.Buchungsdatum.Year] += gewinn;
            else gewinne[t.Buchungsdatum.Year] = gewinn;
        }
    }
}
