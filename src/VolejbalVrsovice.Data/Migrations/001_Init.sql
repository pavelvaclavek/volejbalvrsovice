-- 001_Init – úvodní schéma (zadání, sekce 6 a 9)
--
-- Konvence ukládání:
--   peníze   INTEGER v haléřích (nikdy REAL)
--   datum    TEXT 'yyyy-MM-dd'
--   čas dne  TEXT 'HH:mm'
--   okamžik  TEXT 'yyyy-MM-ddTHH:mm:ssZ' (UTC)
--   ano/ne   INTEGER 0/1
--   výčty    INTEGER (hodnoty viz Core\Entities\Enums.cs)
--
-- Auditní sloupce CreatedAt/CreatedBy/ModifiedAt/ModifiedBy vyplňuje repozitář;
-- CreatedBy/ModifiedBy = AppUser.Id, NULL = systém (seed).
-- Zápis "X IS date(X)" ověřuje, že X je platné datum v kanonickém tvaru.

CREATE TABLE Meta (
    "Key" TEXT NOT NULL PRIMARY KEY,
    Value TEXT NOT NULL
) WITHOUT ROWID;

CREATE TABLE Country (
    Code TEXT NOT NULL PRIMARY KEY CHECK (length(Code) = 2 AND Code = upper(Code)),   -- ISO 3166-1 alpha-2
    Name TEXT NOT NULL
) WITHOUT ROWID;

CREATE TABLE AppUser (
    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    Email              TEXT    NOT NULL CHECK (length(Email) BETWEEN 3 AND 256 AND Email = lower(Email)),
    DisplayName        TEXT    NOT NULL CHECK (length(DisplayName) BETWEEN 1 AND 100),
    Role               INTEGER NOT NULL CHECK (Role IN (0, 1)),                 -- 0 = Uživatel, 1 = Správce
    IsActive           INTEGER NOT NULL CHECK (IsActive IN (0, 1)),
    MustChangePassword INTEGER NOT NULL CHECK (MustChangePassword IN (0, 1)),
    LastLoginAt        TEXT,
    CreatedAt          TEXT    NOT NULL,
    CreatedBy          INTEGER REFERENCES AppUser (Id),
    ModifiedAt         TEXT    NOT NULL,
    ModifiedBy         INTEGER REFERENCES AppUser (Id)
);

CREATE UNIQUE INDEX UX_AppUser_Email ON AppUser (Email);

CREATE TABLE Team (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    Name          TEXT    NOT NULL CHECK (length(Name) BETWEEN 1 AND 100),
    Description   TEXT    CHECK (Description IS NULL OR length(Description) <= 500),
    ContactPerson TEXT,
    ContactEmail  TEXT,
    ContactPhone  TEXT,
    IsActive      INTEGER NOT NULL CHECK (IsActive IN (0, 1)),
    CreatedAt     TEXT    NOT NULL,
    CreatedBy     INTEGER REFERENCES AppUser (Id),
    ModifiedAt    TEXT    NOT NULL,
    ModifiedBy    INTEGER REFERENCES AppUser (Id)
);

CREATE UNIQUE INDEX UX_Team_Name ON Team (Name COLLATE NOCASE);

CREATE TABLE TrainingSchedule (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    TeamId     INTEGER NOT NULL REFERENCES Team (Id),
    DayOfWeek  INTEGER NOT NULL CHECK (DayOfWeek BETWEEN 0 AND 6),             -- System.DayOfWeek: 0 = neděle … 6 = sobota
    TimeFrom   TEXT    NOT NULL CHECK (TimeFrom GLOB '[0-2][0-9]:[0-5][0-9]'),
    TimeTo     TEXT    NOT NULL CHECK (TimeTo GLOB '[0-2][0-9]:[0-5][0-9]'),
    Address    TEXT    NOT NULL CHECK (length(Address) BETWEEN 1 AND 300),     -- název haly + adresa
    Note       TEXT,
    CreatedAt  TEXT    NOT NULL,
    CreatedBy  INTEGER REFERENCES AppUser (Id),
    ModifiedAt TEXT    NOT NULL,
    ModifiedBy INTEGER REFERENCES AppUser (Id),
    CHECK (TimeTo > TimeFrom)
);

CREATE INDEX IX_TrainingSchedule_TeamId ON TrainingSchedule (TeamId);

CREATE TABLE Player (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    FirstName       TEXT    NOT NULL CHECK (length(FirstName) BETWEEN 1 AND 100),
    LastName        TEXT    NOT NULL CHECK (length(LastName) BETWEEN 1 AND 100),
    Gender          INTEGER CHECK (Gender IN (1, 2)),                           -- 1 = muž, 2 = žena
    BirthDate       TEXT    NOT NULL CHECK (BirthDate IS date(BirthDate)),
    NationalityCode TEXT    NOT NULL DEFAULT 'CZ' REFERENCES Country (Code),
    Email           TEXT,
    Phone           TEXT,
    Address         TEXT,
    JoinedOn        TEXT    NOT NULL CHECK (JoinedOn IS date(JoinedOn)),        -- nástup do klubu
    IsActive        INTEGER NOT NULL CHECK (IsActive IN (0, 1)),                -- aktivní v klubu
    HeightCm        INTEGER CHECK (HeightCm BETWEEN 100 AND 250),
    Note            TEXT,
    CreatedAt       TEXT    NOT NULL,
    CreatedBy       INTEGER REFERENCES AppUser (Id),
    ModifiedAt      TEXT    NOT NULL,
    ModifiedBy      INTEGER REFERENCES AppUser (Id),
    CHECK (Email IS NOT NULL OR Phone IS NOT NULL)                              -- alespoň jeden kontakt
);

CREATE INDEX IX_Player_Name ON Player (LastName, FirstName);

CREATE TABLE TeamMembership (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    PlayerId   INTEGER NOT NULL REFERENCES Player (Id),
    TeamId     INTEGER NOT NULL REFERENCES Team (Id),
    JoinedOn   TEXT    NOT NULL CHECK (JoinedOn IS date(JoinedOn)),             -- nástup do družstva
    LeftOn     TEXT    CHECK (LeftOn IS NULL OR LeftOn IS date(LeftOn)),        -- vyplněno = v družstvu již není
    CreatedAt  TEXT    NOT NULL,
    CreatedBy  INTEGER REFERENCES AppUser (Id),
    ModifiedAt TEXT    NOT NULL,
    ModifiedBy INTEGER REFERENCES AppUser (Id),
    CHECK (LeftOn IS NULL OR LeftOn >= JoinedOn)
);

CREATE UNIQUE INDEX UX_TeamMembership_Player_Team ON TeamMembership (PlayerId, TeamId);
CREATE INDEX IX_TeamMembership_TeamId ON TeamMembership (TeamId);

CREATE TABLE Contribution (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    PlayerId       INTEGER NOT NULL REFERENCES Player (Id),
    Date           TEXT    NOT NULL CHECK (Date IS date(Date)),                 -- datum přijetí
    Amount         INTEGER NOT NULL,                                            -- haléře
    PaymentMethod  INTEGER NOT NULL CHECK (PaymentMethod IN (1, 2, 3)),         -- 1 = bankovní převod, 2 = hotovost, 3 = převod salda
    VariableSymbol TEXT    CHECK (VariableSymbol IS NULL OR length(VariableSymbol) BETWEEN 1 AND 10),
    BankReference  TEXT    CHECK (BankReference IS NULL OR length(BankReference) <= 100),
    Note           TEXT,
    CreatedAt      TEXT    NOT NULL,
    CreatedBy      INTEGER REFERENCES AppUser (Id),
    ModifiedAt     TEXT    NOT NULL,
    ModifiedBy     INTEGER REFERENCES AppUser (Id),
    -- skutečný příjem je vždy kladný; převod salda může být i záporný (dluh), ale nikdy 0
    CHECK ((PaymentMethod = 3 AND Amount <> 0) OR (PaymentMethod <> 3 AND Amount > 0)),
    -- variabilní symbol jen u bankovního převodu
    CHECK (VariableSymbol IS NULL OR PaymentMethod = 1)
);

CREATE INDEX IX_Contribution_PlayerId ON Contribution (PlayerId);
CREATE INDEX IX_Contribution_Date ON Contribution (Date);

-- nejvýše jeden „Převod salda“ na hráče
CREATE UNIQUE INDEX UX_Contribution_BalanceTransfer ON Contribution (PlayerId) WHERE PaymentMethod = 3;

CREATE TABLE Event (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Name        TEXT    NOT NULL CHECK (length(Name) BETWEEN 1 AND 150),
    Date        TEXT    NOT NULL CHECK (Date IS date(Date)),
    DateTo      TEXT    CHECK (DateTo IS NULL OR DateTo IS date(DateTo)),       -- jen u vícedenní akce
    Location    TEXT    CHECK (Location IS NULL OR length(Location) <= 300),
    DefaultCost INTEGER NOT NULL CHECK (DefaultCost >= 0),                      -- haléře, výchozí náklad na účastníka
    Description TEXT,
    IsClosed    INTEGER NOT NULL CHECK (IsClosed IN (0, 1)),
    CreatedAt   TEXT    NOT NULL,
    CreatedBy   INTEGER REFERENCES AppUser (Id),
    ModifiedAt  TEXT    NOT NULL,
    ModifiedBy  INTEGER REFERENCES AppUser (Id),
    CHECK (DateTo IS NULL OR DateTo >= Date)
);

CREATE INDEX IX_Event_Date ON Event (Date);

CREATE TABLE EventParticipant (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    EventId          INTEGER NOT NULL REFERENCES Event (Id),
    PlayerId         INTEGER NOT NULL REFERENCES Player (Id),
    Cost             INTEGER NOT NULL CHECK (Cost >= 0),                        -- haléře
    IsCostOverridden INTEGER NOT NULL CHECK (IsCostOverridden IN (0, 1)),       -- 1 = náklad ručně změněn
    Note             TEXT,
    CreatedAt        TEXT    NOT NULL,
    CreatedBy        INTEGER REFERENCES AppUser (Id),
    ModifiedAt       TEXT    NOT NULL,
    ModifiedBy       INTEGER REFERENCES AppUser (Id)
);

CREATE UNIQUE INDEX UX_EventParticipant_Event_Player ON EventParticipant (EventId, PlayerId);
CREATE INDEX IX_EventParticipant_PlayerId ON EventParticipant (PlayerId);

CREATE TABLE AuditLog (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp TEXT    NOT NULL,
    UserId    INTEGER REFERENCES AppUser (Id),
    Entity    TEXT    NOT NULL,
    EntityId  INTEGER NOT NULL,
    Action    TEXT    NOT NULL,
    OldValues TEXT,                                                             -- JSON
    NewValues TEXT                                                              -- JSON
);

CREATE INDEX IX_AuditLog_Entity ON AuditLog (Entity, EntityId);

-- Saldo hráče = součet příspěvků − součet nákladů na akce. Nikde se neukládá, jediná definice je tady.
CREATE VIEW PlayerBalance AS
SELECT t.PlayerId,
       t.ContributionTotal,
       t.CostTotal,
       t.ContributionTotal - t.CostTotal AS Balance
FROM (
    SELECT p.Id AS PlayerId,
           COALESCE((SELECT SUM(c.Amount) FROM Contribution c WHERE c.PlayerId = p.Id), 0) AS ContributionTotal,
           COALESCE((SELECT SUM(ep.Cost) FROM EventParticipant ep WHERE ep.PlayerId = p.Id), 0) AS CostTotal
    FROM Player p
) t;

-- Číselník zemí (ISO 3166-1 alpha-2, české názvy)
INSERT INTO Country (Code, Name) VALUES
    ('AD', 'Andorra'),
    ('AE', 'Spojené arabské emiráty'),
    ('AF', 'Afghánistán'),
    ('AG', 'Antigua a Barbuda'),
    ('AI', 'Anguilla'),
    ('AL', 'Albánie'),
    ('AM', 'Arménie'),
    ('AO', 'Angola'),
    ('AQ', 'Antarktida'),
    ('AR', 'Argentina'),
    ('AS', 'Americká Samoa'),
    ('AT', 'Rakousko'),
    ('AU', 'Austrálie'),
    ('AW', 'Aruba'),
    ('AX', 'Ålandy'),
    ('AZ', 'Ázerbájdžán'),
    ('BA', 'Bosna a Hercegovina'),
    ('BB', 'Barbados'),
    ('BD', 'Bangladéš'),
    ('BE', 'Belgie'),
    ('BF', 'Burkina Faso'),
    ('BG', 'Bulharsko'),
    ('BH', 'Bahrajn'),
    ('BI', 'Burundi'),
    ('BJ', 'Benin'),
    ('BL', 'Svatý Bartoloměj'),
    ('BM', 'Bermudy'),
    ('BN', 'Brunej'),
    ('BO', 'Bolívie'),
    ('BQ', 'Bonaire, Svatý Eustach a Saba'),
    ('BR', 'Brazílie'),
    ('BS', 'Bahamy'),
    ('BT', 'Bhútán'),
    ('BV', 'Bouvetův ostrov'),
    ('BW', 'Botswana'),
    ('BY', 'Bělorusko'),
    ('BZ', 'Belize'),
    ('CA', 'Kanada'),
    ('CC', 'Kokosové ostrovy'),
    ('CD', 'Demokratická republika Kongo'),
    ('CF', 'Středoafrická republika'),
    ('CG', 'Republika Kongo'),
    ('CH', 'Švýcarsko'),
    ('CI', 'Pobřeží slonoviny'),
    ('CK', 'Cookovy ostrovy'),
    ('CL', 'Chile'),
    ('CM', 'Kamerun'),
    ('CN', 'Čína'),
    ('CO', 'Kolumbie'),
    ('CR', 'Kostarika'),
    ('CU', 'Kuba'),
    ('CV', 'Kapverdy'),
    ('CW', 'Curaçao'),
    ('CX', 'Vánoční ostrov'),
    ('CY', 'Kypr'),
    ('CZ', 'Česko'),
    ('DE', 'Německo'),
    ('DJ', 'Džibutsko'),
    ('DK', 'Dánsko'),
    ('DM', 'Dominika'),
    ('DO', 'Dominikánská republika'),
    ('DZ', 'Alžírsko'),
    ('EC', 'Ekvádor'),
    ('EE', 'Estonsko'),
    ('EG', 'Egypt'),
    ('EH', 'Západní Sahara'),
    ('ER', 'Eritrea'),
    ('ES', 'Španělsko'),
    ('ET', 'Etiopie'),
    ('FI', 'Finsko'),
    ('FJ', 'Fidži'),
    ('FK', 'Falklandy'),
    ('FM', 'Mikronésie'),
    ('FO', 'Faerské ostrovy'),
    ('FR', 'Francie'),
    ('GA', 'Gabon'),
    ('GB', 'Spojené království'),
    ('GD', 'Grenada'),
    ('GE', 'Gruzie'),
    ('GF', 'Francouzská Guyana'),
    ('GG', 'Guernsey'),
    ('GH', 'Ghana'),
    ('GI', 'Gibraltar'),
    ('GL', 'Grónsko'),
    ('GM', 'Gambie'),
    ('GN', 'Guinea'),
    ('GP', 'Guadeloupe'),
    ('GQ', 'Rovníková Guinea'),
    ('GR', 'Řecko'),
    ('GS', 'Jižní Georgie a Jižní Sandwichovy ostrovy'),
    ('GT', 'Guatemala'),
    ('GU', 'Guam'),
    ('GW', 'Guinea-Bissau'),
    ('GY', 'Guyana'),
    ('HK', 'Hongkong'),
    ('HM', 'Heardův ostrov a McDonaldovy ostrovy'),
    ('HN', 'Honduras'),
    ('HR', 'Chorvatsko'),
    ('HT', 'Haiti'),
    ('HU', 'Maďarsko'),
    ('ID', 'Indonésie'),
    ('IE', 'Irsko'),
    ('IL', 'Izrael'),
    ('IM', 'Ostrov Man'),
    ('IN', 'Indie'),
    ('IO', 'Britské indickooceánské území'),
    ('IQ', 'Irák'),
    ('IR', 'Írán'),
    ('IS', 'Island'),
    ('IT', 'Itálie'),
    ('JE', 'Jersey'),
    ('JM', 'Jamajka'),
    ('JO', 'Jordánsko'),
    ('JP', 'Japonsko'),
    ('KE', 'Keňa'),
    ('KG', 'Kyrgyzstán'),
    ('KH', 'Kambodža'),
    ('KI', 'Kiribati'),
    ('KM', 'Komory'),
    ('KN', 'Svatý Kryštof a Nevis'),
    ('KP', 'Severní Korea'),
    ('KR', 'Jižní Korea'),
    ('KW', 'Kuvajt'),
    ('KY', 'Kajmanské ostrovy'),
    ('KZ', 'Kazachstán'),
    ('LA', 'Laos'),
    ('LB', 'Libanon'),
    ('LC', 'Svatá Lucie'),
    ('LI', 'Lichtenštejnsko'),
    ('LK', 'Šrí Lanka'),
    ('LR', 'Libérie'),
    ('LS', 'Lesotho'),
    ('LT', 'Litva'),
    ('LU', 'Lucembursko'),
    ('LV', 'Lotyšsko'),
    ('LY', 'Libye'),
    ('MA', 'Maroko'),
    ('MC', 'Monako'),
    ('MD', 'Moldavsko'),
    ('ME', 'Černá Hora'),
    ('MF', 'Svatý Martin (francouzská část)'),
    ('MG', 'Madagaskar'),
    ('MH', 'Marshallovy ostrovy'),
    ('MK', 'Severní Makedonie'),
    ('ML', 'Mali'),
    ('MM', 'Myanmar (Barma)'),
    ('MN', 'Mongolsko'),
    ('MO', 'Macao'),
    ('MP', 'Severní Mariany'),
    ('MQ', 'Martinik'),
    ('MR', 'Mauritánie'),
    ('MS', 'Montserrat'),
    ('MT', 'Malta'),
    ('MU', 'Mauricius'),
    ('MV', 'Maledivy'),
    ('MW', 'Malawi'),
    ('MX', 'Mexiko'),
    ('MY', 'Malajsie'),
    ('MZ', 'Mosambik'),
    ('NA', 'Namibie'),
    ('NC', 'Nová Kaledonie'),
    ('NE', 'Niger'),
    ('NF', 'Norfolk'),
    ('NG', 'Nigérie'),
    ('NI', 'Nikaragua'),
    ('NL', 'Nizozemsko'),
    ('NO', 'Norsko'),
    ('NP', 'Nepál'),
    ('NR', 'Nauru'),
    ('NU', 'Niue'),
    ('NZ', 'Nový Zéland'),
    ('OM', 'Omán'),
    ('PA', 'Panama'),
    ('PE', 'Peru'),
    ('PF', 'Francouzská Polynésie'),
    ('PG', 'Papua Nová Guinea'),
    ('PH', 'Filipíny'),
    ('PK', 'Pákistán'),
    ('PL', 'Polsko'),
    ('PM', 'Saint-Pierre a Miquelon'),
    ('PN', 'Pitcairnovy ostrovy'),
    ('PR', 'Portoriko'),
    ('PS', 'Palestina'),
    ('PT', 'Portugalsko'),
    ('PW', 'Palau'),
    ('PY', 'Paraguay'),
    ('QA', 'Katar'),
    ('RE', 'Réunion'),
    ('RO', 'Rumunsko'),
    ('RS', 'Srbsko'),
    ('RU', 'Rusko'),
    ('RW', 'Rwanda'),
    ('SA', 'Saúdská Arábie'),
    ('SB', 'Šalomounovy ostrovy'),
    ('SC', 'Seychely'),
    ('SD', 'Súdán'),
    ('SE', 'Švédsko'),
    ('SG', 'Singapur'),
    ('SH', 'Svatá Helena, Ascension a Tristan da Cunha'),
    ('SI', 'Slovinsko'),
    ('SJ', 'Špicberky a Jan Mayen'),
    ('SK', 'Slovensko'),
    ('SL', 'Sierra Leone'),
    ('SM', 'San Marino'),
    ('SN', 'Senegal'),
    ('SO', 'Somálsko'),
    ('SR', 'Surinam'),
    ('SS', 'Jižní Súdán'),
    ('ST', 'Svatý Tomáš a Princův ostrov'),
    ('SV', 'Salvador'),
    ('SX', 'Svatý Martin (nizozemská část)'),
    ('SY', 'Sýrie'),
    ('SZ', 'Svazijsko (Eswatini)'),
    ('TC', 'Turks a Caicos'),
    ('TD', 'Čad'),
    ('TF', 'Francouzská jižní a antarktická území'),
    ('TG', 'Togo'),
    ('TH', 'Thajsko'),
    ('TJ', 'Tádžikistán'),
    ('TK', 'Tokelau'),
    ('TL', 'Východní Timor'),
    ('TM', 'Turkmenistán'),
    ('TN', 'Tunisko'),
    ('TO', 'Tonga'),
    ('TR', 'Turecko'),
    ('TT', 'Trinidad a Tobago'),
    ('TV', 'Tuvalu'),
    ('TW', 'Tchaj-wan'),
    ('TZ', 'Tanzanie'),
    ('UA', 'Ukrajina'),
    ('UG', 'Uganda'),
    ('UM', 'Menší odlehlé ostrovy USA'),
    ('US', 'Spojené státy americké'),
    ('UY', 'Uruguay'),
    ('UZ', 'Uzbekistán'),
    ('VA', 'Vatikán'),
    ('VC', 'Svatý Vincenc a Grenadiny'),
    ('VE', 'Venezuela'),
    ('VG', 'Britské Panenské ostrovy'),
    ('VI', 'Americké Panenské ostrovy'),
    ('VN', 'Vietnam'),
    ('VU', 'Vanuatu'),
    ('WF', 'Wallis a Futuna'),
    ('WS', 'Samoa'),
    ('YE', 'Jemen'),
    ('YT', 'Mayotte'),
    ('ZA', 'Jihoafrická republika'),
    ('ZM', 'Zambie'),
    ('ZW', 'Zimbabwe');
