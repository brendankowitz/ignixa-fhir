import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import HomepageFeatures from '@site/src/components/HomepageFeatures';

import Heading from '@theme/Heading';
import styles from './index.module.css';

function ProjectStatus() {
  return (
    <section style={{
      padding: '2rem 0',
      textAlign: 'center',
    }}>
      <div className="container">
        <div style={{
          backgroundColor: 'var(--ifm-color-emphasis-100)',
          border: '1px solid var(--ifm-color-emphasis-300)',
          borderRadius: '8px',
          padding: '1.5rem 2rem',
          maxWidth: '800px',
          margin: '0 auto',
        }}>
          <p style={{
            margin: 0,
            color: 'var(--ifm-color-emphasis-800)',
            fontSize: '0.95rem',
            lineHeight: '1.6',
          }}>
            <strong>Project Status:</strong> Advanced Research / Reference Implementation.
            This is a personal project exploring "next-gen" architecture. It supports and tests
            advanced parts of the FHIR specification but is not a supported enterprise product.
          </p>
        </div>
      </div>
    </section>
  );
}

function HomepageHeader() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <header className={clsx('hero hero--primary', styles.heroBanner)}>
      <div className="container">
        <Heading as="h1" className="hero__title">
          {siteConfig.title}
        </Heading>
        <p className="hero__subtitle">{siteConfig.tagline}</p>
        <div className={styles.buttons}>
          <Link
            className="button button--lg"
            style={{backgroundColor: '#fff', color: '#023e8a', border: 'none'}}
            to="/docs/getting-started/installation">
            Get Started 🚀
          </Link>
          <Link
            className="button button--lg"
            style={{marginLeft: '1rem', backgroundColor: 'transparent', color: '#fff', border: '2px solid #fff'}}
            to="/docs/server/overview">
            FHIR Server 🏥
          </Link>
          <Link
            className="button button--lg"
            style={{marginLeft: '1rem', backgroundColor: 'transparent', color: '#fff', border: '2px solid #fff'}}
            to="/docs/core-sdk/overview">
            Core SDK 📦
          </Link>
        </div>
      </div>
    </header>
  );
}

export default function Home() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <Layout
      title="Enterprise FHIR Server for .NET"
      description="Ignixa is a high-performance, multi-tenant, cloud-native FHIR server built on .NET 9. Features comprehensive FHIR R4/R5 support, three-tier validation, streaming serialization, and modular Core SDK packages.">
      <HomepageHeader />
      <main>
        <HomepageFeatures />
        <ProjectStatus />
      </main>
    </Layout>
  );
}
