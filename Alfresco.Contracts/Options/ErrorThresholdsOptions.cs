namespace Alfresco.Contracts.Options
{
    /// <summary>
    /// Configuration options for error thresholds that determine when migration should stop
    /// </summary>
    public class ErrorThresholdsOptions
    {
        /// <summary>
        /// Configuration section name in appsettings.json
        /// </summary>
        public const string SectionName = "ErrorThresholds";

        /// <summary>
        /// Maximum number of timeout errors before stopping migration
        /// Default: 10
        /// </summary>
        public int MaxTimeoutsBeforeStop { get; set; } = 10;

        /// <summary>
        /// Maximum number of retry exhausted errors before stopping migration
        /// Default: 50
        /// </summary>
        public int MaxRetryFailuresBeforeStop { get; set; } = 50;

        /// <summary>
        /// Maximum total errors (timeouts + retry failures) before stopping migration
        /// Default: 100
        /// </summary>
        public int MaxTotalErrorsBeforeStop { get; set; } = 100;

        /// <summary>
        /// Validates configuration values
        /// </summary>
        public void Validate()
        {
            if (MaxTimeoutsBeforeStop <= 0)
                throw new ArgumentException("MaxTimeoutsBeforeStop must be greater than 0", nameof(MaxTimeoutsBeforeStop));

            if (MaxRetryFailuresBeforeStop <= 0)
                throw new ArgumentException("MaxRetryFailuresBeforeStop must be greater than 0", nameof(MaxRetryFailuresBeforeStop));

            if (MaxTotalErrorsBeforeStop <= 0)
                throw new ArgumentException("MaxTotalErrorsBeforeStop must be greater than 0", nameof(MaxTotalErrorsBeforeStop));
        }
    }
}
