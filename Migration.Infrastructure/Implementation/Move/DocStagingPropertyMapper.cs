using Alfresco.Contracts.Oracle.Models;
using System;
using System.Collections.Generic;

namespace Migration.Infrastructure.Implementation.Move
{
    internal static class DocStagingPropertyMapper
    {
        public static Dictionary<string, object> BuildMigrationProperties(DocStaging doc)
        {
            var props = new Dictionary<string, object>();

            // ecm:docType
            if (!string.IsNullOrWhiteSpace(doc.NewDocumentCode))
                props["ecm:docType"] = doc.NewDocumentCode;
            else if (!string.IsNullOrWhiteSpace(doc.DocumentType))
                props["ecm:docType"] = doc.DocumentType;

            // ecm:docClientType
            if (!string.IsNullOrWhiteSpace(doc.FinalDocumentType))
                props["ecm:docClientType"] = doc.FinalDocumentType;

            // ecm:coreId
            if (!string.IsNullOrWhiteSpace(doc.CoreId))
                props["ecm:coreId"] = doc.CoreId;

            // ecm:docTypeName — primarno NewDocumentName, fallback DocDescription
            if (!string.IsNullOrWhiteSpace(doc.NewDocumentName))
                props["ecm:docTypeName"] = doc.NewDocumentName;
            else if (!string.IsNullOrWhiteSpace(doc.DocDescription))
                props["ecm:docTypeName"] = doc.DocDescription;

            // ecm:naziv
            if (!string.IsNullOrWhiteSpace(doc.DocDescription))
                props["ecm:naziv"] = doc.DocDescription;

            // ecm:docStatus
            if (!string.IsNullOrWhiteSpace(doc.Status))
                props["ecm:docStatus"] = doc.NewAlfrescoStatus;

            // ecm:docCategory
            if (!string.IsNullOrWhiteSpace(doc.CategoryCode))
                props["ecm:docCategory"] = doc.CategoryCode;

            // ecm:docCategoryName
            if (!string.IsNullOrWhiteSpace(doc.CategoryName))
                props["ecm:docCategoryName"] = doc.CategoryName;

            // ecm:docDossierId + ecm:folderId
            if (!string.IsNullOrWhiteSpace(doc.DestinationFolderId))
            {
                props["ecm:docDossierId"] = doc.DestinationFolderId;
                props["ecm:folderId"]     = doc.DestinationFolderId;
            }

            // ecm:docDossierType — derived from DossierDestFolderId prefix
            if (!string.IsNullOrWhiteSpace(doc.DossierDestFolderId))
            {
                string type = doc.DossierDestFolderId switch
                {
                    var s when s.StartsWith("ACC-", StringComparison.OrdinalIgnoreCase) => "ACC",
                    var s when s.StartsWith("PI-",  StringComparison.OrdinalIgnoreCase) => "PI",
                    var s when s.StartsWith("LE-",  StringComparison.OrdinalIgnoreCase) => "LE",
                    var s when s.StartsWith("DE-",  StringComparison.OrdinalIgnoreCase) => "D",
                    _ => ""
                };

                if (!string.IsNullOrEmpty(type))
                    props["ecm:docDossierType"] = type;
            }

            return props;
        }
    }
}
