using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.Infrastructure.Storage;

/// <summary>
/// Forward-only schema history. Migrations are never edited once released: a correction is added
/// as a new migration so an existing database can always reach the current version.
///
/// Money is stored as TEXT holding an invariant decimal string. That keeps cents exact and avoids
/// the silent precision loss of a REAL column, at the cost of arithmetic happening in C# rather
/// than SQL — which is where the rounding rules live anyway.
/// </summary>
public static class BudgetSchema
{
    public static IReadOnlyList<SchemaMigration> Migrations { get; } =
    [
        new SchemaMigration(
            1,
            "Application metadata table",
            """
            CREATE TABLE app_metadata (
                key   TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            ) STRICT;
            """),

        new SchemaMigration(
            2,
            "Audit trail table",
            """
            CREATE TABLE audit_log (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                occurred_utc TEXT NOT NULL,
                event_name   TEXT NOT NULL,
                detail       TEXT
            ) STRICT;
            """),

        new SchemaMigration(
            3,
            "Audit trail time index",
            "CREATE INDEX ix_audit_log_occurred_utc ON audit_log (occurred_utc);"),

        new SchemaMigration(
            4,
            "Household and members",
            """
            CREATE TABLE household (
                id                           INTEGER PRIMARY KEY CHECK (id = 1),
                name                         TEXT NOT NULL,
                currency_symbol              TEXT NOT NULL,
                date_format                  TEXT NOT NULL,
                minimum_balance_reserve      TEXT NOT NULL,
                minimum_breathing_room       TEXT NOT NULL,
                emergency_fund_target_months TEXT NOT NULL
            ) STRICT;

            CREATE TABLE household_member (
                id                           TEXT PRIMARY KEY NOT NULL,
                name                         TEXT NOT NULL,
                is_dependant                 INTEGER NOT NULL CHECK (is_dependant IN (0, 1)),
                date_of_birth                TEXT,
                personal_allowance           TEXT NOT NULL,
                personal_allowance_frequency INTEGER NOT NULL
            ) STRICT;
            """),

        new SchemaMigration(
            5,
            "Accounts and balances",
            """
            CREATE TABLE bank_account (
                id              TEXT PRIMARY KEY NOT NULL,
                name            TEXT NOT NULL,
                current_balance TEXT NOT NULL,
                is_primary      INTEGER NOT NULL CHECK (is_primary IN (0, 1)),
                owner_member_id TEXT REFERENCES household_member (id) ON DELETE SET NULL,
                updated_on      TEXT NOT NULL
            ) STRICT;
            """),

        new SchemaMigration(
            6,
            "Income sources",
            """
            CREATE TABLE income_source (
                id                          TEXT PRIMARY KEY NOT NULL,
                kind                        TEXT NOT NULL CHECK (kind IN ('hourly', 'salary', 'variable', 'mileage')),
                name                        TEXT NOT NULL,
                member_id                   TEXT NOT NULL REFERENCES household_member (id) ON DELETE CASCADE,
                pay_frequency               INTEGER NOT NULL,
                anchor_pay_date             TEXT NOT NULL,
                is_taxable                  INTEGER NOT NULL CHECK (is_taxable IN (0, 1)),
                is_active                   INTEGER NOT NULL CHECK (is_active IN (0, 1)),
                hourly_rate                 TEXT,
                hours_conservative          TEXT,
                hours_normal                TEXT,
                hours_optimistic            TEXT,
                overtime_conservative       TEXT,
                overtime_normal             TEXT,
                overtime_optimistic         TEXT,
                overtime_multiplier         TEXT,
                paid_leave_hours             TEXT,
                unpaid_leave_hours          TEXT,
                annual_salary               TEXT,
                amount_conservative         TEXT,
                amount_normal               TEXT,
                amount_optimistic           TEXT,
                miles_per_period            TEXT,
                rate_per_mile               TEXT
            ) STRICT;

            CREATE INDEX ix_income_source_member ON income_source (member_id);
            """),

        new SchemaMigration(
            7,
            "Payslip history",
            """
            CREATE TABLE payslip (
                id                        TEXT PRIMARY KEY NOT NULL,
                income_source_id          TEXT NOT NULL REFERENCES income_source (id) ON DELETE CASCADE,
                pay_date                  TEXT NOT NULL,
                gross_pay                 TEXT NOT NULL,
                pre_tax_deductions        TEXT NOT NULL,
                federal_withholding       TEXT NOT NULL,
                state_withholding         TEXT NOT NULL,
                social_security           TEXT NOT NULL,
                medicare                  TEXT NOT NULL,
                post_tax_deductions       TEXT NOT NULL,
                non_taxable_reimbursements TEXT NOT NULL,
                hours_worked              TEXT NOT NULL,
                overtime_hours_worked     TEXT NOT NULL
            ) STRICT;

            CREATE INDEX ix_payslip_source_date ON payslip (income_source_id, pay_date);

            -- A payslip cannot exist twice for the same source and date. This is the duplicate
            -- guard for imports, enforced by the database rather than only in code.
            CREATE UNIQUE INDEX ux_payslip_source_date ON payslip (income_source_id, pay_date);
            """),

        new SchemaMigration(
            8,
            "Expenses and split rules",
            """
            CREATE TABLE expense_item (
                id                     TEXT PRIMARY KEY NOT NULL,
                name                   TEXT NOT NULL,
                category_name          TEXT NOT NULL,
                category_is_built_in   INTEGER NOT NULL CHECK (category_is_built_in IN (0, 1)),
                expected_amount        TEXT NOT NULL,
                frequency              INTEGER NOT NULL,
                anchor_due_date        TEXT NOT NULL,
                necessity              INTEGER NOT NULL,
                variability            INTEGER NOT NULL,
                ownership              INTEGER NOT NULL,
                split_method           INTEGER,
                autopay_anchor_date    TEXT,
                is_paused              INTEGER NOT NULL CHECK (is_paused IN (0, 1)),
                ends_on                TEXT,
                notes                  TEXT,
                annual_increase_percent TEXT NOT NULL
            ) STRICT;

            CREATE TABLE expense_split_participant (
                expense_item_id TEXT NOT NULL REFERENCES expense_item (id) ON DELETE CASCADE,
                position        INTEGER NOT NULL,
                member_id       TEXT NOT NULL REFERENCES household_member (id) ON DELETE CASCADE,
                percentage      TEXT,
                fixed_amount    TEXT,
                PRIMARY KEY (expense_item_id, position)
            ) STRICT;
            """),

        new SchemaMigration(
            9,
            "Expense transactions",
            """
            CREATE TABLE expense_transaction (
                id                   TEXT PRIMARY KEY NOT NULL,
                expense_item_id      TEXT REFERENCES expense_item (id) ON DELETE SET NULL,
                occurred_on          TEXT NOT NULL,
                description          TEXT NOT NULL,
                amount               TEXT NOT NULL,
                category_name        TEXT NOT NULL,
                category_is_built_in INTEGER NOT NULL CHECK (category_is_built_in IN (0, 1)),
                is_refund            INTEGER NOT NULL CHECK (is_refund IN (0, 1)),
                split_parent_id      TEXT REFERENCES expense_transaction (id) ON DELETE CASCADE,
                is_confirmed         INTEGER NOT NULL CHECK (is_confirmed IN (0, 1)),
                notes                TEXT,
                import_batch_id      TEXT,
                duplicate_key        TEXT
            ) STRICT;

            CREATE INDEX ix_transaction_date ON expense_transaction (occurred_on);
            CREATE INDEX ix_transaction_item ON expense_transaction (expense_item_id);
            CREATE INDEX ix_transaction_batch ON expense_transaction (import_batch_id);

            -- Identical date, amount and description almost always means the same transaction was
            -- imported twice. The index makes that impossible rather than merely unlikely.
            CREATE UNIQUE INDEX ux_transaction_duplicate_key ON expense_transaction (duplicate_key)
                WHERE duplicate_key IS NOT NULL;
            """),

        new SchemaMigration(
            10,
            "Benefits and insurance",
            """
            CREATE TABLE benefit_plan (
                id                             TEXT PRIMARY KEY NOT NULL,
                name                           TEXT NOT NULL,
                kind                           INTEGER NOT NULL,
                member_id                      TEXT NOT NULL REFERENCES household_member (id) ON DELETE CASCADE,
                coverage                       INTEGER NOT NULL,
                employee_premium_per_period    TEXT NOT NULL,
                premium_frequency              INTEGER NOT NULL,
                employer_contribution_per_period TEXT NOT NULL,
                tax_treatment                  INTEGER NOT NULL,
                deductible                     TEXT NOT NULL,
                out_of_pocket_maximum          TEXT NOT NULL,
                coverage_amount                TEXT NOT NULL,
                effective_date                 TEXT,
                renewal_date                   TEXT,
                notes                          TEXT
            ) STRICT;

            CREATE TABLE benefit_beneficiary (
                benefit_plan_id TEXT NOT NULL REFERENCES benefit_plan (id) ON DELETE CASCADE,
                position        INTEGER NOT NULL,
                name            TEXT NOT NULL,
                PRIMARY KEY (benefit_plan_id, position)
            ) STRICT;
            """),

        new SchemaMigration(
            11,
            "Payroll profiles and deductions",
            """
            CREATE TABLE payroll_profile (
                member_id                        TEXT PRIMARY KEY NOT NULL
                                                 REFERENCES household_member (id) ON DELETE CASCADE,
                tax_year                         INTEGER NOT NULL,
                filing_status                    INTEGER NOT NULL,
                qualifying_children              INTEGER NOT NULL,
                other_dependants                 INTEGER NOT NULL,
                other_annual_income              TEXT NOT NULL,
                annual_deductions                TEXT NOT NULL,
                extra_withholding_per_period     TEXT NOT NULL,
                multiple_jobs_checked            INTEGER NOT NULL CHECK (multiple_jobs_checked IN (0, 1)),
                is_non_resident_alien            INTEGER NOT NULL CHECK (is_non_resident_alien IN (0, 1)),
                state_flat_rate                  TEXT NOT NULL,
                retirement_employee_percent      TEXT NOT NULL,
                retirement_employer_percent      TEXT NOT NULL,
                retirement_employer_limit_percent TEXT NOT NULL,
                retirement_vesting_years         INTEGER NOT NULL,
                retirement_is_roth               INTEGER NOT NULL CHECK (retirement_is_roth IN (0, 1)),
                years_of_service                 TEXT NOT NULL
            ) STRICT;

            CREATE TABLE payroll_deduction (
                id            TEXT PRIMARY KEY NOT NULL,
                member_id     TEXT NOT NULL REFERENCES household_member (id) ON DELETE CASCADE,
                name          TEXT NOT NULL,
                amount_per_period TEXT NOT NULL,
                tax_treatment INTEGER NOT NULL,
                position      INTEGER NOT NULL
            ) STRICT;

            CREATE INDEX ix_payroll_deduction_member ON payroll_deduction (member_id);
            """),

        new SchemaMigration(
            12,
            "Debts",
            """
            CREATE TABLE debt_account (
                id                      TEXT PRIMARY KEY NOT NULL,
                name                    TEXT NOT NULL,
                kind                    INTEGER NOT NULL,
                balance                 TEXT NOT NULL,
                annual_percentage_rate  TEXT NOT NULL,
                minimum_payment         TEXT NOT NULL,
                promotional_rate        TEXT,
                promotional_rate_ends   TEXT,
                owner_member_id         TEXT REFERENCES household_member (id) ON DELETE SET NULL,
                due_day_of_month        INTEGER
            ) STRICT;
            """),

        new SchemaMigration(
            13,
            "Savings funds and goals",
            """
            CREATE TABLE savings_fund (
                id                      TEXT PRIMARY KEY NOT NULL,
                name                    TEXT NOT NULL,
                purpose                 INTEGER NOT NULL,
                current_balance         TEXT NOT NULL,
                target_amount           TEXT,
                target_date             TEXT,
                planned_contribution    TEXT NOT NULL,
                contribution_frequency  INTEGER NOT NULL,
                priority                INTEGER NOT NULL,
                owner_member_id         TEXT REFERENCES household_member (id) ON DELETE SET NULL,
                is_revolving            INTEGER NOT NULL CHECK (is_revolving IN (0, 1)),
                notes                   TEXT
            ) STRICT;

            CREATE TABLE goal (
                id                     TEXT PRIMARY KEY NOT NULL,
                name                   TEXT NOT NULL,
                target_amount          TEXT NOT NULL,
                target_date            TEXT NOT NULL,
                current_amount         TEXT NOT NULL,
                priority               INTEGER NOT NULL,
                scope                  INTEGER NOT NULL,
                owner_member_id        TEXT REFERENCES household_member (id) ON DELETE SET NULL,
                planned_contribution   TEXT NOT NULL,
                contribution_frequency INTEGER NOT NULL,
                source_scenario_id     TEXT,
                notes                  TEXT
            ) STRICT;
            """),

        new SchemaMigration(
            14,
            "Planning scenarios",
            """
            CREATE TABLE scenario (
                id                          TEXT PRIMARY KEY NOT NULL,
                name                        TEXT NOT NULL,
                kind                        INTEGER NOT NULL,
                proposed_start_date         TEXT NOT NULL,
                advertised_amount           TEXT NOT NULL,
                advertised_frequency        INTEGER NOT NULL,
                status                      INTEGER NOT NULL,
                worst_case_uplift_percent   TEXT NOT NULL,
                best_case_reduction_percent TEXT NOT NULL,
                notes                       TEXT,
                override_reason             TEXT,
                created_utc                 TEXT NOT NULL
            ) STRICT;

            CREATE TABLE scenario_line (
                scenario_id     TEXT NOT NULL REFERENCES scenario (id) ON DELETE CASCADE,
                position        INTEGER NOT NULL,
                area            TEXT NOT NULL,
                name            TEXT NOT NULL,
                question        TEXT NOT NULL,
                state           INTEGER NOT NULL,
                amount          TEXT NOT NULL,
                frequency       INTEGER NOT NULL,
                typical_amount  TEXT,
                is_critical     INTEGER NOT NULL CHECK (is_critical IN (0, 1)),
                is_non_cash     INTEGER NOT NULL CHECK (is_non_cash IN (0, 1)),
                notes           TEXT,
                PRIMARY KEY (scenario_id, position)
            ) STRICT;
            """),

        new SchemaMigration(
            15,
            "Import batches for undo",
            """
            CREATE TABLE import_batch (
                id           TEXT PRIMARY KEY NOT NULL,
                imported_utc TEXT NOT NULL,
                source_name  TEXT NOT NULL,
                row_count    INTEGER NOT NULL,
                is_reverted  INTEGER NOT NULL CHECK (is_reverted IN (0, 1))
            ) STRICT;
            """),

        new SchemaMigration(
            16,
            "Needs-based guidance and paycheque allocation",
            """
            ALTER TABLE household_member
                ADD COLUMN is_discretionary_eligible INTEGER NOT NULL DEFAULT 0;

            ALTER TABLE expense_transaction
                ADD COLUMN spent_by_member_id TEXT;

            CREATE TABLE allocation_rules (
                id                              INTEGER PRIMARY KEY CHECK (id = 1),
                basis                           INTEGER NOT NULL,
                optimistic_funds_essentials     INTEGER NOT NULL CHECK (optimistic_funds_essentials IN (0, 1)),
                safety_buffer                   TEXT NOT NULL,
                discretionary_method            INTEGER NOT NULL,
                discretionary_percent           TEXT NOT NULL,
                fixed_discretionary_amount      TEXT NOT NULL,
                shared_entertainment            TEXT NOT NULL,
                flexible_savings                TEXT NOT NULL,
                rounding_remainder_member_id    TEXT,
                notes                           TEXT,
                fallback_tier_essential         INTEGER NOT NULL,
                fallback_tier_optional          INTEGER NOT NULL,
                locality_country                TEXT NOT NULL,
                locality_state                  TEXT,
                locality_county                 TEXT,
                locality_city                   TEXT
            ) STRICT;

            CREATE TABLE allocation_member_percent (
                member_id TEXT NOT NULL REFERENCES household_member (id) ON DELETE CASCADE,
                purpose   INTEGER NOT NULL,
                percent   TEXT NOT NULL,
                PRIMARY KEY (member_id, purpose)
            ) STRICT;

            CREATE TABLE allocation_surplus_rule (
                position INTEGER PRIMARY KEY,
                target   INTEGER NOT NULL,
                percent  TEXT NOT NULL
            ) STRICT;

            CREATE TABLE allocation_reduction_step (
                position INTEGER PRIMARY KEY,
                bucket   INTEGER NOT NULL
            ) STRICT;

            CREATE TABLE needs_category_rule (
                category_name       TEXT PRIMARY KEY NOT NULL,
                tier                INTEGER NOT NULL,
                is_always_protected INTEGER NOT NULL CHECK (is_always_protected IN (0, 1)),
                notes               TEXT
            ) STRICT;

            CREATE TABLE grocery_plan (
                id                          TEXT PRIMARY KEY NOT NULL,
                kind                        INTEGER NOT NULL UNIQUE,
                name                        TEXT NOT NULL,
                notes                       TEXT,
                assistance_is_expected      INTEGER NOT NULL CHECK (assistance_is_expected IN (0, 1)),
                assistance_is_suspended     INTEGER NOT NULL CHECK (assistance_is_suspended IN (0, 1)),
                assistance_source           TEXT,
                assistance_weekly_value     TEXT NOT NULL,
                assistance_effective_date   TEXT,
                assistance_review_date      TEXT,
                assistance_notes            TEXT
            ) STRICT;

            CREATE TABLE grocery_plan_assistance_category (
                plan_id       TEXT NOT NULL REFERENCES grocery_plan (id) ON DELETE CASCADE,
                category_name TEXT NOT NULL,
                PRIMARY KEY (plan_id, category_name)
            ) STRICT;

            CREATE TABLE grocery_category (
                id                      TEXT PRIMARY KEY NOT NULL,
                plan_id                 TEXT NOT NULL REFERENCES grocery_plan (id) ON DELETE CASCADE,
                name                    TEXT NOT NULL,
                is_essential            INTEGER NOT NULL CHECK (is_essential IN (0, 1)),
                suggested_weekly        TEXT NOT NULL,
                low_range               TEXT NOT NULL,
                typical_range           TEXT NOT NULL,
                comfortable_range       TEXT NOT NULL,
                weekly_limit            TEXT NOT NULL,
                carried_forward         TEXT NOT NULL,
                rollover                INTEGER NOT NULL,
                supplied_by_assistance  INTEGER NOT NULL CHECK (supplied_by_assistance IN (0, 1)),
                guidance_source         TEXT NOT NULL,
                guidance_effective_date TEXT,
                confidence              INTEGER NOT NULL,
                review_required         INTEGER NOT NULL CHECK (review_required IN (0, 1)),
                notes                   TEXT
            ) STRICT;

            CREATE TABLE cost_guidance (
                id               TEXT PRIMARY KEY NOT NULL,
                country          TEXT NOT NULL,
                state            TEXT,
                county           TEXT,
                city             TEXT,
                adults           INTEGER NOT NULL,
                children         INTEGER NOT NULL,
                category         TEXT NOT NULL,
                effective_date   TEXT NOT NULL,
                source_name      TEXT NOT NULL,
                source_type      INTEGER NOT NULL,
                observed_on      TEXT,
                low_range        TEXT NOT NULL,
                typical_range    TEXT NOT NULL,
                comfortable_range TEXT NOT NULL,
                confidence       INTEGER NOT NULL,
                last_reviewed_on TEXT,
                review_by_date   TEXT,
                notes            TEXT
            ) STRICT;

            CREATE TABLE obligation_reserve (
                id            TEXT PRIMARY KEY NOT NULL,
                obligation_id TEXT NOT NULL UNIQUE,
                kind          INTEGER NOT NULL,
                reserved      TEXT NOT NULL,
                updated_on    TEXT NOT NULL,
                is_protected  INTEGER NOT NULL CHECK (is_protected IN (0, 1)),
                notes         TEXT
            ) STRICT;

            CREATE TABLE personal_transfer (
                id                  TEXT PRIMARY KEY NOT NULL,
                from_member_id      TEXT NOT NULL REFERENCES household_member (id) ON DELETE CASCADE,
                to_member_id        TEXT NOT NULL REFERENCES household_member (id) ON DELETE CASCADE,
                amount              TEXT NOT NULL,
                transfer_date       TEXT NOT NULL,
                purpose             TEXT,
                is_recurring        INTEGER NOT NULL CHECK (is_recurring IN (0, 1)),
                recurring_frequency INTEGER NOT NULL
            ) STRICT;

            CREATE TABLE reimbursement_offset (
                id                TEXT PRIMARY KEY NOT NULL,
                income_source_id  TEXT NOT NULL REFERENCES income_source (id) ON DELETE CASCADE,
                obligation_id     TEXT,
                kind              INTEGER NOT NULL,
                offset_per_period TEXT NOT NULL,
                notes             TEXT
            ) STRICT;
            """),

        new SchemaMigration(
            17,
            "Versioned tax, starter guidance, products and international transfers",
            """
            CREATE TABLE allocation_funding_step (
                position INTEGER PRIMARY KEY,
                bucket   INTEGER NOT NULL
            ) STRICT;

            ALTER TABLE cost_guidance ADD COLUMN source_url TEXT;
            ALTER TABLE cost_guidance ADD COLUMN source_identifier TEXT;
            ALTER TABLE cost_guidance ADD COLUMN assumptions TEXT;
            ALTER TABLE cost_guidance ADD COLUMN unit_or_frequency TEXT;
            ALTER TABLE cost_guidance ADD COLUMN is_derived INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE cost_guidance ADD COLUMN source_total TEXT;
            ALTER TABLE cost_guidance ADD COLUMN allocation_method TEXT;
            ALTER TABLE cost_guidance ADD COLUMN calculation TEXT;
            ALTER TABLE cost_guidance ADD COLUMN pack_version TEXT;
            ALTER TABLE cost_guidance ADD COLUMN user_selected_amount TEXT;
            ALTER TABLE cost_guidance ADD COLUMN price_kind INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE cost_guidance ADD COLUMN freshness_days INTEGER NOT NULL DEFAULT 90;

            ALTER TABLE payroll_profile ADD COLUMN uses_utah_withholding INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE payroll_profile ADD COLUMN tax_residency INTEGER NOT NULL DEFAULT 4;
            ALTER TABLE payroll_profile ADD COLUMN withholding_rule_version TEXT;

            ALTER TABLE payslip ADD COLUMN tax_year INTEGER;
            ALTER TABLE payslip ADD COLUMN rule_set_version TEXT;

            CREATE TABLE product (
                id                    TEXT PRIMARY KEY NOT NULL,
                name                  TEXT NOT NULL,
                category              TEXT NOT NULL,
                subcategory           TEXT,
                brand                 TEXT,
                description           TEXT,
                package_size          TEXT NOT NULL,
                quantity              TEXT NOT NULL,
                unit_of_measure       TEXT NOT NULL,
                units_per_package     TEXT NOT NULL,
                upc_or_sku            TEXT,
                is_essential          INTEGER NOT NULL CHECK (is_essential IN (0, 1)),
                tier                  INTEGER NOT NULL,
                purchase_frequency    INTEGER NOT NULL,
                is_preferred          INTEGER NOT NULL CHECK (is_preferred IN (0, 1)),
                is_active             INTEGER NOT NULL CHECK (is_active IN (0, 1)),
                tax_category          INTEGER NOT NULL,
                notes                 TEXT
            ) STRICT;

            CREATE TABLE product_substitute (
                product_id    TEXT NOT NULL REFERENCES product (id) ON DELETE CASCADE,
                substitute_id TEXT NOT NULL REFERENCES product (id) ON DELETE CASCADE,
                PRIMARY KEY (product_id, substitute_id)
            ) STRICT;

            CREATE TABLE price_observation (
                id                         TEXT PRIMARY KEY NOT NULL,
                product_id                 TEXT NOT NULL REFERENCES product (id) ON DELETE CASCADE,
                retailer                   TEXT,
                store_location             TEXT,
                country                    TEXT NOT NULL,
                state                      TEXT,
                county                     TEXT,
                city                       TEXT,
                shelf_price                TEXT NOT NULL,
                loyalty_or_sale_price      TEXT,
                tax_amount                 TEXT,
                deposit_or_fee             TEXT,
                estimated_checkout_price   TEXT,
                actual_checkout_price      TEXT,
                observed_on                TEXT NOT NULL,
                sale_starts_on             TEXT,
                sale_ends_on               TEXT,
                source_name                TEXT NOT NULL,
                source_type                INTEGER NOT NULL,
                confidence                 INTEGER NOT NULL,
                price_kind                 INTEGER NOT NULL,
                review_by_date             TEXT,
                tax_category               INTEGER NOT NULL,
                tax_collection             INTEGER NOT NULL,
                excise_embedded            INTEGER NOT NULL CHECK (excise_embedded IN (0, 1)),
                notes                      TEXT
            ) STRICT;

            CREATE TABLE sales_tax_rule (
                id                          TEXT PRIMARY KEY NOT NULL,
                state                       TEXT NOT NULL,
                county                      TEXT,
                city                        TEXT,
                category                    INTEGER NOT NULL,
                rate                        TEXT NOT NULL,
                effective_date              TEXT NOT NULL,
                review_by_date              TEXT,
                official_source             TEXT NOT NULL,
                rule_set_version            TEXT NOT NULL,
                collection                  INTEGER NOT NULL,
                excise_already_in_shelf     INTEGER NOT NULL CHECK (excise_already_in_shelf IN (0, 1))
            ) STRICT;

            CREATE TABLE exchange_rate_quote (
                id                      TEXT PRIMARY KEY NOT NULL,
                source_currency         TEXT NOT NULL,
                destination_currency    TEXT NOT NULL,
                rate                    TEXT NOT NULL,
                timestamp_utc           TEXT NOT NULL,
                source_name             TEXT NOT NULL,
                mid_market_rate         TEXT,
                provider_rate           TEXT,
                actual_rate_received    TEXT,
                spread                  TEXT,
                provider_fee            TEXT NOT NULL
            ) STRICT;

            CREATE TABLE international_transfer (
                id                              TEXT PRIMARY KEY NOT NULL,
                transfer_date                   TEXT NOT NULL,
                sender                          TEXT NOT NULL,
                recipient                       TEXT NOT NULL,
                sending_country                 TEXT NOT NULL,
                receiving_country               TEXT NOT NULL,
                sending_owner_member_id         TEXT,
                receiving_owner_member_id       TEXT,
                relationship                    INTEGER NOT NULL,
                source_currency                 TEXT NOT NULL,
                destination_currency            TEXT NOT NULL,
                amount_sent                     TEXT NOT NULL,
                exchange_rate                   TEXT NOT NULL,
                amount_received                 TEXT NOT NULL,
                provider                        TEXT,
                provider_fee                    TEXT NOT NULL,
                sending_bank_fee                TEXT NOT NULL,
                intermediary_bank_fee           TEXT NOT NULL,
                recipient_fee                   TEXT NOT NULL,
                exchange_rate_spread            TEXT,
                purpose                         INTEGER NOT NULL,
                custom_purpose                  TEXT,
                tax_classification              INTEGER NOT NULL,
                reporting_classification        TEXT,
                linked_transfer_id              TEXT,
                supporting_document_id          TEXT,
                hierarchy_tier                  INTEGER,
                frequency                       INTEGER NOT NULL,
                is_fixed_destination            INTEGER NOT NULL CHECK (is_fixed_destination IN (0, 1)),
                exchange_rate_safety_margin     TEXT,
                due_date                        TEXT,
                review_status                   INTEGER NOT NULL,
                classification_corrected_on     TEXT,
                classification_correction_note  TEXT,
                original_classification         INTEGER,
                original_exchange_rate          TEXT,
                notes                           TEXT
            ) STRICT;

            CREATE TABLE support_commitment (
                id                      TEXT PRIMARY KEY NOT NULL,
                name                    TEXT NOT NULL,
                frequency               INTEGER NOT NULL,
                fixed_usd_to_send       TEXT,
                fixed_zar_to_receive    TEXT,
                expected_rate_usd_zar   TEXT NOT NULL,
                expected_fees           TEXT NOT NULL,
                safety_margin           TEXT NOT NULL,
                due_date                TEXT,
                hierarchy_tier          INTEGER
            ) STRICT;

            CREATE TABLE foreign_account (
                id                          TEXT PRIMARY KEY NOT NULL,
                owner_member_id             TEXT,
                institution                 TEXT NOT NULL,
                country                     TEXT NOT NULL,
                nickname                    TEXT NOT NULL,
                currency                    TEXT NOT NULL,
                maximum_calendar_year_balance TEXT,
                year_end_balance            TEXT,
                reporting_exchange_rate     TEXT,
                usd_equivalent              TEXT,
                has_financial_interest      INTEGER NOT NULL CHECK (has_financial_interest IN (0, 1)),
                has_signature_authority     INTEGER NOT NULL CHECK (has_signature_authority IN (0, 1)),
                opened_on                   TEXT,
                closed_on                   TEXT,
                review_status               INTEGER NOT NULL
            ) STRICT;

            CREATE TABLE supporting_document (
                id                      TEXT PRIMARY KEY NOT NULL,
                title                   TEXT NOT NULL,
                kind                    TEXT NOT NULL,
                document_date           TEXT,
                encrypted_storage_key   TEXT,
                notes                   TEXT
            ) STRICT;

            CREATE TABLE guidance_pack_state (
                id              INTEGER PRIMARY KEY CHECK (id = 1),
                imported_version TEXT,
                imported_on     TEXT
            ) STRICT;
            """),

        new SchemaMigration(
            18,
            "Import confirmation flags and outstanding questions",
            """
            ALTER TABLE income_source ADD COLUMN pay_schedule_confirmed INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE income_source ADD COLUMN notes TEXT;
            ALTER TABLE expense_item ADD COLUMN due_date_unknown INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE benefit_plan ADD COLUMN is_confirmed INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE grocery_plan ADD COLUMN cash_amount_needs_confirmation INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE allocation_rules ADD COLUMN discretionary_percent_configured INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE payroll_profile ADD COLUMN w4_is_complete INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE payroll_profile ADD COLUMN notes TEXT;
            ALTER TABLE household ADD COLUMN forecast_horizon_months INTEGER NOT NULL DEFAULT 12;

            CREATE TABLE outstanding_question (
                id         TEXT PRIMARY KEY NOT NULL,
                message    TEXT NOT NULL,
                sort_order INTEGER NOT NULL
            ) STRICT;
            """),

        new SchemaMigration(
            19,
            "Member archive, income dates, due-date adjustment and audit detail",
            """
            ALTER TABLE household_member ADD COLUMN notes TEXT;
            ALTER TABLE household_member ADD COLUMN is_archived INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE household_member ADD COLUMN effective_from TEXT;
            ALTER TABLE household_member ADD COLUMN effective_to TEXT;
            ALTER TABLE household_member ADD COLUMN participates_in_shared_costs INTEGER NOT NULL DEFAULT 1;

            ALTER TABLE income_source ADD COLUMN starts_on TEXT;
            ALTER TABLE income_source ADD COLUMN ends_on TEXT;
            ALTER TABLE income_source ADD COLUMN income_role INTEGER NOT NULL DEFAULT 0;

            ALTER TABLE expense_item ADD COLUMN is_archived INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE expense_item ADD COLUMN due_date_adjustment INTEGER NOT NULL DEFAULT 0;

            ALTER TABLE audit_log ADD COLUMN operation TEXT;
            ALTER TABLE audit_log ADD COLUMN record_type TEXT;
            ALTER TABLE audit_log ADD COLUMN record_id TEXT;
            ALTER TABLE audit_log ADD COLUMN before_summary TEXT;
            ALTER TABLE audit_log ADD COLUMN after_summary TEXT;
            ALTER TABLE audit_log ADD COLUMN user_note TEXT;
            """)
    ];

    public static int LatestVersion => Migrations.Count;
}
