using CsvHelper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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
        public string Isin { get; set; }
        public decimal Anteile { get; set; }
        public decimal Abrechnungspreis { get; set; }
        public decimal Ruecknamepreis { get; set; }
        public string Currency { get; set; }
        public decimal Ausgabeaufschlage { get; set; }
        public decimal AbrechnungsbetragInEur { get; set; }

        public decimal Devisenkurs { get; set; }
        public decimal RuecknamepreisInEur { get { return Ruecknamepreis / Devisenkurs; } }
        public decimal AbrechnungspreisInEur { get { return Abrechnungspreis / Devisenkurs; } }
    }

    class Program
    {
        static void Main()
        {
            var ts = new List<Transaction>();

            // Alles deutsch.
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var config = new CsvHelper.Configuration.CsvConfiguration
            {
                Delimiter = ";",
                Encoding = Encoding.UTF8,
                CultureInfo = new System.Globalization.CultureInfo("de-DE")
            };

            var csv = new CsvReader(File.OpenText("alle.csv"), config);
            while (csv.Read()) {
                var type = csv.GetField<string>("Transaktion").ToLower();

                if (type == "erträgnis ohne wiederanlage" ||
                    type.Contains("zugang") ||
                    type.Contains("merge") ||
                    type.Contains("übertrag")) continue;

				ts.Add(new Transaction
				{ 
					Type = type,
					Buchungsdatum = csv.GetField<DateTime>("Buchungsdatum"),
					Isin = csv.GetField<string>("ISIN"),
					Anteile = csv.GetField<decimal>("Anteile"),
					Abrechnungspreis = csv.GetField<decimal>("Abrechnungspreis"),
					Ruecknamepreis = csv.GetField<decimal>("Rücknahmepreis"),
					Currency = csv.GetField<string>("Fondswährung"),
					Devisenkurs = csv.GetField<decimal>("Devisenkurs"),
					Ausgabeaufschlage = csv.GetField<decimal>("Ausgabeaufschlag in EUR"),
					AbrechnungsbetragInEur = csv.GetField<decimal>("Abrechnungsbetrag in EUR")
				});
            }

			// Liste mit allen Fonds (ISIN) ermitteln.
            var isins = ts.Select(t => t.Isin).Distinct();

			// Pro Fond einen eigenen Stack mit chronologischen Buchungen mit je 1 Anteil.
            var fonds = ts
                .OrderBy(t => t.Buchungsdatum)
                .GroupBy(t => t.Isin)
                .ToDictionary(g => g.Key, g => g.ToList());

			// Header schreiben.
			Console.WriteLine("Buchungsdatum\tISIN\tTyp\tAnteile\tRücknamepreis €\tAbrechnungspreis €\tAbrechnungsbetrag €\tGewinn\tKeSt Gewinn\tKeSt-Frei\tErklärung(Rücknahmepreis*Anteile)");

            foreach (var isin in isins) {
                _CalculateYearlyWinFor(fonds[isin]); 
            }

        }

        /// <summary>
        /// Berechnet den Jährlichen gewinn für den übergebenen Fonds.
        /// </summary>
        /// <param name="transactions"></param>
        /// <returns></returns>
        private static Dictionary<int, decimal> _CalculateYearlyWinFor(IEnumerable<Transaction> transactions)
        {
            var gewinne = new Dictionary<int, decimal>();
            var kestGewinne = new Dictionary<int, decimal>();
            var depot = new Queue<Transaction>();
			var kestFreiDatum = new DateTime(2011, 1, 1);

			// Zur sicherheit sortieren wir die Transaktionen nochmal nach Buchungsdatum.
            var ts = transactions.OrderBy(t => t.Buchungsdatum);
            
            foreach (var t in ts) {
                Console.Write("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}", 
                    t.Buchungsdatum.ToShortDateString(), t.Isin, t.Type, t.Anteile, t.RuecknamepreisInEur, t.AbrechnungspreisInEur, t.AbrechnungsbetragInEur);
                
                if (t.Type.Contains("verkauf") || t.Type.Contains("entgeltbelastung")) {
                    var anteile = t.Anteile;
                    var gewinn = 0.0M;
                    var kestGewinn = 0.0M;
                    var kestFrei = 0.0M;
                    var kestErklaerung = "";

                    // Wenn das Depot leer ist, dann ist hier ein Fehler passiert, 
                    // weil uns Käufe aus der Vergangenheit fehlen!
                    if (!depot.Any())
                    {
                        Console.WriteLine("\tERROR\tERROR\tERROR\tERROR");
                        continue;
                    }

                    while (anteile > 0.0M) {
                        var last = depot.Peek();
                        if (last.Anteile > anteile) {

                            // Gewinn berechnen.
                            var kpreis = anteile * last.RuecknamepreisInEur;
                            var vpreis = anteile * t.RuecknamepreisInEur;
                            var zwischenGewinn = vpreis - kpreis;
                            gewinn += zwischenGewinn;
                            if (last.Buchungsdatum >= kestFreiDatum)
                            {
                                kestGewinn += zwischenGewinn;
                            }
                            else
                            {
                                kestFrei += zwischenGewinn;
                            }

                            kestErklaerung += string.Format("Kauf {2:d}: {3:C} ({0} von {4} Anteile x {1:C}) => Gewinn: {5:C}+++", 
                                anteile, 
                                last.RuecknamepreisInEur, 
                                last.Buchungsdatum,
                                kpreis,
                                last.Anteile,
                                zwischenGewinn);

                            // Anteile aktualisieren.
                            last.Anteile -= anteile;
                            anteile = 0;
                        }
                        else
                        {
                            // Gewinn berechnen.
                            var kpreis = last.Anteile * last.RuecknamepreisInEur;
                            var vpreis = last.Anteile * t.RuecknamepreisInEur;
                            var zwischenGewinn = vpreis - kpreis;
                            gewinn += zwischenGewinn;
                            if (last.Buchungsdatum >= kestFreiDatum)
                            {
                                kestGewinn += zwischenGewinn;
                            }
                            else
                            {
                                kestFrei += zwischenGewinn;
                            }

                            kestErklaerung += string.Format("Kauf {2:d}: {3:C} ({0} Anteile x {1:C}) => Gewinn: {4:C}+++",
                                last.Anteile,
                                last.RuecknamepreisInEur,
                                last.Buchungsdatum,
                                kpreis,
                                zwischenGewinn);


                            depot.Dequeue();
                            anteile -= last.Anteile;
                        }
                    }

                    // Zum jährlichen Gewinn hinzufügen.
                    if (t.Type.Contains("entgeltbelastung")) {
						Console.WriteLine("\t0\t0\t0\t");
                    }
                    else {
                        _mehrGewinn(gewinne, t, gewinn);
                        _mehrGewinn(kestGewinne, t, kestGewinn);

                        Console.Write("\t{0}", gewinn);
                        Console.Write("\t{0}", kestGewinn);
                        Console.Write("\t{0}", kestFrei);
                        Console.WriteLine("\t{0}", kestErklaerung);
                    }
                }
                else if (t.Type.Contains("kauf") || t.Type == "erträgnis") {
                    Console.WriteLine("\t0\t0\t0\t");
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
