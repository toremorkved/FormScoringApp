namespace NewsScoreApp.Services.AI;

/// <summary>Generates a realistic Norwegian clinical note for the POC "paste text" demo, so the
/// user doesn't need to type anything to try the AI auto-fill feature. One fixed sample per
/// recipe id is enough for a POC; real usage would let the user paste their own text.</summary>
public static class SampleTranscripts
{
    public static string ForRecipe(string recipeId) => recipeId switch
    {
        "news2" => News2Sample,
        "qsofa" => QsofaSample,
        "gcs" => GcsSample,
        "braden" => BradenSample,
        "nrs2002" => Nrs2002Sample,
        "downton" => DowntonSample,
        "esas" => EsasSample,
        "nihss" => NihssSample,
        "epikrise" => EpikriseSample,
        _ => GenericSample,
    };

    private const string News2Sample =
        "Tilsyn hos pasient kl. 14:20 etter sykepleier meldte fra om økt tungpust. " +
        "Pasienten er våken, orientert og klar og orientert for tid og sted. " +
        "Respirasjonsfrekvens målt til 24 per minutt, noe økt siden forrige måling. " +
        "SpO2 målt til 91 % på romluft, startet derfor nesekateter med 2 liter oksygen, " +
        "ny måling viser bedring. Puls 112, regelmessig. Blodtrykk: systolisk 108 mmHg. " +
        "Temperatur målt aksillært til 38,4. Pasienten er engstelig, men samarbeider godt " +
        "under undersøkelsen. Plan: revurdering om 30 minutter, kontakt lege ved forverring.";

    private const string QsofaSample =
        "Tilkalt til sengepost grunnet mistanke om sepsis hos pasient med kjent urinveisinfeksjon. " +
        "Ved undersøkelse: respirasjonsfrekvens telt til 23 per minutt. Blodtrykk målt til " +
        "systolisk 95 mmHg, noe lavt for pasienten. Pasienten er klart forvirret og kjenner " +
        "ikke igjen dato eller hvor hun befinner seg, endret bevissthetsnivå sammenlignet med " +
        "tidligere i dag. Konfererer med bakvakt om videre oppfølging og eventuell overflytning.";

    private const string GcsSample =
        "Tilsyn etter fall i korridor med hodetraume. Ved ankomst åpner pasienten øynene kun " +
        "når han blir tilsnakket høyt, ikke spontant. Verbalt svarer han med forvirret og noe " +
        "usammenhengende tale, klarer ikke gjøre rede for hva som skjedde. Motorisk lokaliserer " +
        "han tydelig mot smertestimulus i brystbenet og prøver å dytte hånden vekk. Overvåkes " +
        "tett med hyppige revurderinger, CT caput rekvirert.";

    private const string BradenSample =
        "Trykksårvurdering ved innleggelse. Pasienten har nedsatt sensorisk oppfatning og " +
        "reagerer bare svakt på smerte eller ubehag i deler av kroppen. Huden er av og til " +
        "fuktig grunnet noe svetting. Pasienten går av og til korte turer med rullator, men " +
        "tilbringer mesteparten av tiden i seng eller stol. Kan bevege seg noe i sengen selv, " +
        "men mobiliteten er litt begrenset. Ernæringsmessig spiser pasienten sjelden hele " +
        "porsjoner og virker å ha et utilstrekkelig matinntak. Ved forflytning glir pasienten " +
        "noe ned i sengen og trenger hjelp til å justere posisjon, friksjon er et potensielt problem.";

    private const string Nrs2002Sample =
        "Ernæringsscreening ved innkomst. Pasienten oppgir ufrivillig vekttap på over 5 % " +
        "de siste tre månedene, og har bare fått i seg om lag halvparten av vanlig matinntak " +
        "siste uke grunnet nedsatt appetitt. Innlagt for forverring av kronisk obstruktiv " +
        "lungesykdom med behov for noe mer oppfølging enn vanlig, ingen intensivbehandling. " +
        "Pasienten er 74 år gammel.";

    private const string DowntonSample =
        "Fallrisikovurdering ved innleggelse. Pasienten forteller at hun falt hjemme for to " +
        "uker siden. Hun bruker flere faste medikamenter, blant annet vanndrivende og " +
        "beroligende tabletter. Ingen kjent sensorisk svekkelse, syn og hørsel er greit. " +
        "Pasienten er klar og orientert for tid og sted. Ved mobilisering er gangen tydelig " +
        "ustøtt og usikker, hun holder seg gjerne til møbler når hun beveger seg.";

    private const string EsasSample =
        "Legenotat, samtale med pasienten om symptomtrykk (ESAS-r). Spør pasienten om hun kan " +
        "tallfeste hvert symptom fra 0 til 10, der 0 er ingen plage og 10 er verst tenkelig. " +
        "Smerte: pasienten oppgir 4, sier det stort sett kjennes som verking i korsryggen. " +
        "Tretthet: svarer 7, kjenner seg svært sliten og orker lite i løpet av dagen. " +
        "Døsighet: sier 3, er noe søvnig etter formiddagens medisiner, men lar seg vekke uten " +
        "problemer. Kvalme: oppgir 2, bare litt kvalm av og til, ingen oppkast. Nedsatt " +
        "matlyst: svarer 6, har spist svært lite de siste dagene og kjenner seg fort mett. " +
        "Tung pust: sier 5, blir andpusten ved korte gåturer til bad. Nedstemthet: oppgir 6, " +
        "forteller at hun har vært mye nedfor og bekymret for utviklingen av sykdommen. " +
        "Engstelse: svarer 5, kjenner uro særlig på kveldstid. Følelse av velvære: sier 6 på " +
        "hvordan hun har det totalt sett for tiden, opplever at plagene går utover " +
        "hverdagen. Plan: revurdere smertelindring og vurdere henvisning til palliativt team.";

    private const string NihssSample =
        "Akuttilsyn (NIHSS) hos pasient innbrakt med mistenkt akutt hjerneslag, symptomdebut for " +
        "ca. 90 minutter siden ifølge pårørende. Ved ankomst ligger pasienten med lukkede øyne og " +
        "reagerer ikke spontant, men våkner og åpner øynene når han blir tilsnakket høyt og " +
        "berørt på skulderen - faller raskt tilbake i søvnighet når stimuleringen opphører. " +
        "Spør pasienten hvilken måned det er og hvor gammel han er: han svarer riktig på " +
        "spørsmålet om måned, men bommer på egen alder og virker usikker. Ber ham deretter lukke " +
        "og åpne øynene, noe han klarer greit, men han får ikke til å knytte og åpne hånden på " +
        "kommando til tross for gjentatte forsøk. Ved undersøkelse av blikkfunksjon sees en " +
        "deviasjon mot høyre som lar seg overstyre når han blir bedt om å følge fingeren til " +
        "undersøkeren. Konfrontasjonstest av synsfelt gir mistanke om delvis utfall i venstre " +
        "synsfelt. Det er en tydelig venstresidig nedre facialisparese med flatere nasolabialfold " +
        "og asymmetrisk smil, men pannen rynkes symmetrisk. Ved styrkeprøve av armene holder " +
        "høyre arm posisjonen fint i ti sekunder, mens venstre arm sildrer nedover og ender opp " +
        "liggende før tiden er ute, uten at den treffer underlaget. Høyre ben holder stilling " +
        "uten problemer, men venstre ben klarer bare å løftes svakt mot tyngdekraften før det " +
        "synker ned mot sengen. Ingen tydelig dysmetri utover det som kan forklares av " +
        "kraftsvikten ved finger-nese-forsøk og hæl-kne-prøve. Sensibilitet testes med lett " +
        "berøring og han kjenner det tydelig svakere på venstre side sammenlignet med høyre, men " +
        "ikke helt borte. Han snakker flytende, men leter noe etter enkelte ord og klarer ikke " +
        "alltid å benevne gjenstander som blir vist frem, samtalen er likevel stort sett mulig å " +
        "følge. Talen er noe sluret og utydelig, men fortsatt forståelig ved nærmere lytting. Ved " +
        "samtidig berøring av begge armer legger han kun merke til berøringen på høyre side og " +
        "ser ut til å overse venstre arm når han blir bedt om å peke på hvor han ble berørt. " +
        "Familien bekrefter at dette er nytt sammenlignet med i morges. Plan: haste-CT caput og " +
        "CT angiografi, kontakt slagteam og vurdering for trombolyse/trombektomi.";

    private const string EpikriseSample =
        "Utskrivningssamtale ved sengekanten dagen før hjemreise, legen går gjennom oppholdet " +
        "sammen med pasienten før epikrisen dikteres. Lege: «Da skal vi oppsummere innleggelsen " +
        "sammen før du reiser hjem.» Pasienten forteller at hun kom inn på legevakt for fem " +
        "dager siden med økende tungpust, hoste med gulgrønt slim og feber opp mot 39 grader " +
        "hjemme, og at det hadde vart i underkant av en uke før hun oppsøkte hjelp. Hun har fra " +
        "før kjent KOLS og forteller at hun ble innlagt for cirka to år siden med en lignende " +
        "episode, i tillegg til at hun har hatt høyt blodtrykk i mange år og bruker faste " +
        "blodtrykkstabletter. Ved innkomst ble det målt SpO2 på 89 % på romluft, " +
        "respirasjonsfrekvens 26, temperatur 38,9, og det ble hørt knatrelyder basalt til høyre " +
        "ved lungeauskultasjon. Blodprøver viste forhøyet CRP og hvite blodlegemer, og " +
        "røntgen thorax som ble tatt samme kveld viste et infiltrat i høyre underlapp forenlig " +
        "med pneumoni. Konklusjonen etter denne gjennomgangen er samfunnservervet pneumoni i " +
        "høyre underlapp, med forverring av kjent KOLS i tillegg. Lege: «Vi startet deg på " +
        "antibiotika og pusteluft med oksygen med en gang, husker du det?» Pasienten bekrefter " +
        "at hun fikk intravenøs antibiotika de tre første døgnene, som ble lagt om til " +
        "tablettbehandling da hun ble bedre, samt oksygentilskudd på nesekateter som gradvis ble " +
        "trappet ned, og ekstra inhalasjoner med bronkodilaterende medisin i tillegg til sine " +
        "faste KOLS-medisiner. Fysioterapeut var innom to ganger for slimmobilisering og " +
        "pusteøvelser. Hun forteller selv at feberen gikk ned etter to døgn og at pusten ble " +
        "gradvis lettere utover oppholdet, selv om hun fortsatt ble litt andpusten ved " +
        "mobilisering til badet i går. Kontrollmåling i dag viser SpO2 på 96 % på romluft og " +
        "ingen feber siste to døgn. Lege: «Du er klar for å reise hjem i dag, i god bedring.» " +
        "Ved gjennomgang av medisinlisten legges antibiotikakuren til for fem dager videre " +
        "hjemme, mens de faste KOLS-inhalasjonene og blodtrykkstablettene videreføres uendret. " +
        "Avslutningsvis avtales det at fastlegen tar en oppfølgingstime om én uke for å høre " +
        "hvordan det går, og at hjemmesykepleien kobles inn de første dagene for å bistå med " +
        "administrering av antibiotikakuren. Pasienten og pårørende får med seg skriftlig " +
        "informasjon om varselsymptomer som skal føre til rask ny kontakt med lege.";

    private const string GenericSample =
        "Kort tilsynsnotat: pasienten er stabil, ingen nye funn av betydning ved dagens " +
        "undersøkelse. Fortsett gjeldende behandling og følg opp ved neste visitt.";
}
