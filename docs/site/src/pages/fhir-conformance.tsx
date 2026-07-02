import React from 'react';
import Layout from '@theme/Layout';
import FhirConformanceDashboard from '@site/src/components/FhirConformanceDashboard';

export default function FhirConformancePage(): JSX.Element {
  return (
    <Layout
      title="FHIR Conformance Report"
      description="Latest Ignixa FHIR R4 TestScript conformance results"
    >
      <FhirConformanceDashboard />
    </Layout>
  );
}
