namespace ExitInterviewAgent.Records;

// Coarse context bands and interview metadata enums. Wire names are the schema's enum values; a test
// asserts they match schemas/exit-interview-record.v1.schema.json (rationale: ADR-0007).

/// <summary>How long the person worked for the employer.</summary>
public enum TenureBand { LessThan6Months, SixToTwelveMonths, OneToThreeYears, ThreeToFiveYears, FiveToTenYears, OverTenYears }

/// <summary>Seniority, deliberately only four coarse levels.</summary>
public enum SeniorityBand { Junior, Mid, Senior, Management }

/// <summary>Function, deliberately few and broad so that no band is a small group on its own.</summary>
public enum FunctionBand { Engineering, ProductDesign, SalesMarketing, OperationsSupport, CorporateFunctions, Other }

public enum DurationBand { LessThan10Minutes, TenToTwentyMinutes, TwentyToFortyMinutes, OverFortyMinutes }

public enum TurnBand { LessThan10, TenToTwenty, TwentyToForty, OverForty }

public enum Confidence { Low, Medium, High }

/// <summary><c>NoData</c> means not discussed, declined or not answerable; it is not a low rating.</summary>
public enum TopicStatus { NoData, Covered }

public enum Topic { Onboarding, Management, Growth, PayVsPromises, Culture, ReasonForLeaving }
